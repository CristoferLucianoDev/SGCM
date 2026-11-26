using System.Net;
using System.Net.Http.Json;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración de confirmar y rechazar una cita pendiente como médico, contra la API real: el cambio de
/// estado, los correos al paciente, la ocupación del horario, las reglas de plazo y estado, la propiedad de la cita y
/// las decisiones simultáneas.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ConfirmRejectAppointmentTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string DoctorsAppointmentsUrl = "/api/doctors/me/appointments";
    private const string ConfirmSuffix = "confirm";
    private const string RejectSuffix = "reject";
    private const string DecisionReason = "Agenda completa";
    private const string DoctorFullName = "Luis Pérez";
    private const int HoursPastConfirmationDeadline = 47;
    private const int MissingAppointmentId = int.MaxValue;
    private const decimal ConsultationFee = 1500m;

    [Fact]
    public async Task Confirmar_una_cita_pendiente_propia_en_plazo_la_deja_confirmada_y_avisa_solo_al_paciente()
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var response = await ConfirmAsync(doctor.Client, appointmentId);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
        var notification = Factory.Notifications.Sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentConfirmedNotification>();
        notification.Recipient.Email.ShouldBe(patient.Email);
        notification.Appointment.AppointmentId.ShouldBe(appointmentId);
        notification.Appointment.PatientEmail.ShouldBe(patient.Email);
        notification.Appointment.DoctorName.ShouldBe(DoctorFullName);
        notification.Appointment.ConsultationFee.ShouldBe(ConsultationFee);
        notification.Appointment.AppointmentDate.ShouldBe(Today().AddDays(DaysAhead));
        notification.Appointment.StartTime.ShouldBe(new TimeOnly(AppointmentStartHour, 0));
        notification.Appointment.EndTime.ShouldBe(new TimeOnly(AppointmentStartHour + AppointmentHours, 0));
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == doctor.Email);
    }

    [Fact]
    public async Task Rechazar_una_cita_pendiente_propia_en_plazo_con_motivo_la_deja_rechazada_y_avisa_al_paciente()
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var response = await RejectAsync(doctor.Client, appointmentId, DecisionReason);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Rejected, DecisionReason));
        var notification = Factory.Notifications.Sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRejectedNotification>();
        notification.Recipient.Email.ShouldBe(patient.Email);
        notification.RejectionReason.ShouldBe(DecisionReason);
        notification.Appointment.AppointmentId.ShouldBe(appointmentId);
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == doctor.Email);
    }

    [Fact]
    public async Task Tras_rechazar_el_horario_queda_libre_para_otro_paciente()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var other = await SeedPatientAsync();
        using (var reject = await RejectAsync(doctor.Client, appointmentId, DecisionReason))
        {
            await reject.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await RequestSlotAsync(other, doctor, startsAt);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Tras_confirmar_el_horario_sigue_ocupado_para_otro_paciente()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var other = await SeedPatientAsync();
        using (var confirm = await ConfirmAsync(doctor.Client, appointmentId))
        {
            await confirm.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await RequestSlotAsync(other, doctor, startsAt);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.SlotUnavailable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Rechazar_sin_motivo_se_rechaza_y_la_cita_sigue_pendiente_sin_correos(string? reason)
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var response = await RejectAsync(doctor.Client, appointmentId, reason);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonRequired");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Confirmar_una_cita_con_el_plazo_vencido_se_rechaza_y_sigue_pendiente()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, ExpiredStart());

        using var response = await ConfirmAsync(doctor.Client, appointmentId);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.ConfirmationDeadlinePassed");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Rechazar_una_cita_con_el_plazo_vencido_se_rechaza_y_sigue_pendiente()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, ExpiredStart());

        using var response = await RejectAsync(doctor.Client, appointmentId, DecisionReason);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.ConfirmationDeadlinePassed");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Confirmed, null)]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_que_no_esta_pendiente_no_se_puede_confirmar_y_la_fila_no_cambia(
        string seeded, string? statusReason)
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart(), statusReason);

        using var response = await ConfirmAsync(doctor.Client, appointmentId);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.NotPending");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(seeded, statusReason));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Confirmed, null)]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_que_no_esta_pendiente_no_se_puede_rechazar_y_la_fila_no_cambia(
        string seeded, string? statusReason)
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart(), statusReason);

        using var response = await RejectAsync(doctor.Client, appointmentId, DecisionReason);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.NotPending");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(seeded, statusReason));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task La_cita_de_otro_medico_responde_404_al_confirmar_y_al_rechazar_y_no_cambia()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var stranger = await SeedDoctorAsync();

        using var confirm = await ConfirmAsync(stranger.Client, appointmentId);
        using var reject = await RejectAsync(stranger.Client, appointmentId, DecisionReason);

        await AssertErrorAsync(confirm, HttpStatusCode.NotFound, "Appointment.NotFound");
        await AssertErrorAsync(reject, HttpStatusCode.NotFound, "Appointment.NotFound");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Una_cita_que_no_existe_responde_404_al_confirmar_y_al_rechazar()
    {
        var doctor = await SeedDoctorAsync();

        using var confirm = await ConfirmAsync(doctor.Client, MissingAppointmentId);
        using var reject = await RejectAsync(doctor.Client, MissingAppointmentId, DecisionReason);

        await AssertErrorAsync(confirm, HttpStatusCode.NotFound, "Appointment.NotFound");
        await AssertErrorAsync(reject, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task Un_paciente_no_puede_confirmar_ni_rechazar_y_la_cita_sigue_pendiente()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        using var confirm = await ConfirmAsync(patient.Client, appointmentId);
        using var reject = await RejectAsync(patient.Client, appointmentId, DecisionReason);

        await confirm.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await reject.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Sin_token_confirmar_y_rechazar_responden_401()
    {
        using var confirm = await ConfirmAsync(Client, MissingAppointmentId);
        using var reject = await RejectAsync(Client, MissingAppointmentId, DecisionReason);

        await confirm.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await reject.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirmar_y_rechazar_a_la_vez_la_misma_cita_solo_deja_pasar_una_decision()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        var responses = await Task.WhenAll(
            ConfirmAsync(doctor.Client, appointmentId),
            RejectAsync(doctor.Client, appointmentId, DecisionReason));

        try
        {
            responses.Select(response => response.StatusCode)
                .ShouldBe(new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }, ignoreOrder: true);
            var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await rejected.ReadErrorCodeAsync()).ShouldBe("Appointment.NotPending");
            await AssertSingleNotificationMatchesWinnerAsync(appointmentId);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    // La cita queda en el estado de la decisión que pasó y solo esa decisión envía su correo.
    private async Task AssertSingleNotificationMatchesWinnerAsync(int appointmentId)
    {
        var state = await ReadStateAsync(appointmentId);
        var sent = Factory.Notifications.Sent.ShouldHaveSingleItem();
        if (state.Status == Confirmed)
        {
            state.StatusReason.ShouldBeNull();
            sent.ShouldBeOfType<AppointmentConfirmedNotification>();
            return;
        }

        state.ShouldBe(new AppointmentState(Rejected, DecisionReason));
        sent.ShouldBeOfType<AppointmentRejectedNotification>();
    }

    /// <summary>Hora en punto actual más 47 horas: siempre queda a menos de 48 horas, fuera del plazo para decidir.</summary>
    private DateTimeOffset ExpiredStart() => CurrentHourUtc().AddHours(HoursPastConfirmationDeadline);

    private static string DecisionUrl(int appointmentId, string action) =>
        $"{DoctorsAppointmentsUrl}/{appointmentId}/{action}";

    private static Task<HttpResponseMessage> ConfirmAsync(HttpClient client, int appointmentId) =>
        client.PostAsync(DecisionUrl(appointmentId, ConfirmSuffix), content: null);

    private static Task<HttpResponseMessage> RejectAsync(HttpClient client, int appointmentId, string? reason) =>
        client.PostAsJsonAsync(DecisionUrl(appointmentId, RejectSuffix), new { reason });

    private Task<HttpResponseMessage> RequestSlotAsync(PatientSeed patient, DoctorSeed doctor, DateTimeOffset startsAtUtc) =>
        patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = startsAtUtc,
            doctorId = doctor.DoctorId
        });
}
