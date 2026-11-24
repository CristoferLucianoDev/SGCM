using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class CancelAppointmentTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string Reason = "Viaje";
    private const string CallTheDoctorText = "llamar al médico";

    [Theory]
    [InlineData(Pending)]
    [InlineData(Confirmed)]
    public async Task Cancelar_una_cita_propia_en_plazo_la_deja_cancelada_con_su_motivo_y_avisa_solo_al_medico(string seeded)
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, Reason));
        var notification = Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        notification.Recipient.Email.ShouldBe(doctor.Email);
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
        notification.CancellationReason.ShouldBe(Reason);
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == patient.Email);
    }

    [Fact]
    public async Task Cancelar_una_cita_confirmada_sin_motivo_guarda_el_motivo_nulo_y_lo_notifica()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = (string?)null });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, null));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem()
            .CancellationReason.ShouldBeNull();
    }

    [Fact]
    public async Task Tras_cancelar_el_horario_queda_libre_para_otro_paciente()
    {
        var startsAt = InTimeStart();
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var other = await SeedPatientAsync();
        using (var cancel = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }))
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

    [Fact]
    public async Task Fuera_de_plazo_se_rechaza_indicando_que_debe_llamar_al_medico_y_no_avisa_a_nadie()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, PastDeadlineStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        var title = await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.CancellationDeadlinePassed");
        title.ShouldContain(CallTheDoctorText);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_que_no_esta_activa_no_se_puede_cancelar_y_la_fila_no_cambia(string seeded, string? statusReason)
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart(), statusReason);

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.InvalidTransition");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(seeded, statusReason));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task La_cita_de_otro_paciente_responde_404_y_no_cambia()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var stranger = await SeedPatientAsync();

        using var response = await stranger.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Una_cita_que_no_existe_responde_404()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(int.MaxValue), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task Un_motivo_demasiado_largo_se_rechaza_y_la_cita_sigue_pendiente()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var reason = new string('a', Appointment.StatusReasonMaxLength + 1);

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonTooLong");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Un_medico_no_puede_cancelar_citas_desde_este_endpoint()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var login = await LoginSuccessfullyAsync(doctor.Email, Password);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var response = await Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

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
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        var responses = await Task.WhenAll(
            patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }),
            patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }));

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

    private static string CancelUrl(int appointmentId) => $"{AppointmentsUrl}/{appointmentId}/cancel";
}
