using System.Net;
using System.Net.Http.Json;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class CancelAppointmentByDoctorTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string DoctorsAppointmentsUrl = "/api/doctors/me/appointments";
    private const string Reason = "Emergencia familiar";
    private const int HoursInsideShortNotice = 5;
    private const int MinimumWords = 30;
    private const int OneWord = 1;

    [Theory]
    [InlineData(Pending)]
    [InlineData(Confirmed)]
    public async Task El_medico_cancela_su_cita_con_motivo_y_avisa_solo_al_paciente(string seeded)
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart());

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, Reason));
        var notification = Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        notification.Recipient.Email.ShouldBe(patient.Email);
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Doctor);
        notification.CancellationReason.ShouldBe(Reason);
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == doctor.Email);
    }

    [Fact]
    public async Task Tras_cancelar_el_horario_queda_libre_para_un_paciente()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var other = await SeedPatientAsync();
        using (var cancel = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }))
        {
            await cancel.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await other.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt,
            doctorId = doctor.DoctorId
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Sin_motivo_se_rechaza_la_cancelacion_y_la_cita_sigue_pendiente(string? reason)
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonRequired");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Con_poco_aviso_un_motivo_corto_se_rechaza_indicando_el_minimo_de_palabras()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, ShortNoticeStart());

        using var response = await doctor.Client.PostAsJsonAsync(
            CancelUrl(appointmentId), new { reason = Words(MinimumWords - 1) });

        var title = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.ShortNoticeReasonTooShort");
        title.ShouldContain(MinimumWords.ToString());
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Con_poco_aviso_un_motivo_con_las_palabras_minimas_cancela_la_cita()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, ShortNoticeStart());
        var reason = Words(MinimumWords);

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, reason));
    }

    [Fact]
    public async Task A_menos_de_24_horas_pero_sin_poco_aviso_basta_un_motivo_de_una_palabra()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, PastDeadlineStart());
        var reason = Words(OneWord);

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, reason));
    }

    [Fact]
    public async Task Un_motivo_demasiado_largo_se_rechaza_y_la_cita_sigue_pendiente()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var reason = new string('a', Appointment.StatusReasonMaxLength + 1);

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonTooLong");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Theory]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_que_no_esta_activa_no_se_puede_cancelar_y_la_fila_no_cambia(string seeded, string? statusReason)
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart(), statusReason);

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.InvalidTransition");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(seeded, statusReason));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task La_cita_de_otro_medico_responde_404_y_no_cambia()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var stranger = await SeedDoctorAsync();

        using var response = await stranger.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Una_cita_que_no_existe_responde_404()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await doctor.Client.PostAsJsonAsync(CancelUrl(int.MaxValue), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task Un_paciente_no_puede_cancelar_citas_desde_este_endpoint()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var response = await Client.PostAsJsonAsync(CancelUrl(1), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dos_cancelaciones_simultaneas_de_la_misma_cita_solo_dejan_pasar_una()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        var responses = await Task.WhenAll(
            doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }),
            doctor.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }));

        try
        {
            responses.Select(response => response.StatusCode)
                .ShouldBe(new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }, ignoreOrder: true);
            var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await rejected.ReadErrorCodeAsync()).ShouldBe("Appointment.InvalidTransition");
            (await ReadStateAsync(appointmentId)).Status.ShouldBe(Cancelled);
            Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static string CancelUrl(int appointmentId) => $"{DoctorsAppointmentsUrl}/{appointmentId}/cancel";

    /// <summary>Hora en punto actual más 5 horas: siempre queda por debajo del umbral de poco aviso.</summary>
    private DateTimeOffset ShortNoticeStart() => CurrentHourUtc().AddHours(HoursInsideShortNotice);

    private static string Words(int count) => string.Join(" ", Enumerable.Repeat("palabra", count));
}
