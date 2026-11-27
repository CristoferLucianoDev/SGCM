using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración de la reprogramación de una cita por el paciente, contra la API y la base reales: el
/// cambio de horario y de estado, el aviso al médico, la liberación del horario anterior, las reglas de plazo, de
/// ventana y de estado, la propiedad de la cita y la competencia por un mismo horario. Todo va por HTTP, así que el
/// reloj no se mueve y las citas se siembran relativas a la hora actual.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RescheduleAppointmentTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string PatientAppointmentsUrl = "/api/patients/me/appointments";
    private const string RescheduleSuffix = "reschedule";
    private const string RescheduleDeadlinePassedCode = "Appointment.RescheduleDeadlinePassed";
    private const string TooSoonCode = "Appointment.TooSoon";
    private const string NotOnHourCode = "Appointment.NotOnHour";
    private const string SameScheduleCode = "Appointment.SameSchedule";
    private const string StartsAtRequiredCode = "Appointment.StartsAtRequired";
    private const string SlotUnavailableCode = "Appointment.SlotUnavailable";
    private const string PatientOverlapCode = "Appointment.PatientOverlap";
    private const string NotActiveCode = "Appointment.NotActive";
    private const string NotFoundCode = "Appointment.NotFound";
    private const string CallTheDoctorText = "llamar al médico";
    private const int NewDaysAhead = 12;
    private const int TooSoonDaysAhead = 3;
    private const int HoursPastRescheduleDeadline = 47;
    private const int HoursLater = 1;
    private const int HalfHourMinutes = 30;
    private const int MissingAppointmentId = int.MaxValue;

    private sealed record Booking(DoctorSeed Doctor, PatientSeed Patient, int AppointmentId, DateTimeOffset StartsAt);

    private sealed record Schedule(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    [Theory]
    [InlineData(Pending)]
    [InlineData(Confirmed)]
    public async Task Reprogramar_una_cita_propia_en_plazo_a_una_hora_libre_la_deja_pendiente_y_avisa_al_medico(string status)
    {
        var booking = await SeedBookingAsync(status, InTimeStart());
        var newStartsAt = NewStartLocal();

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, newStartsAt);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadScheduleAsync(booking.AppointmentId)).ShouldBe(ScheduleAt(newStartsAt));
        (await ReadStateAsync(booking.AppointmentId)).ShouldBe(new AppointmentState(Pending, null));
        var notification = Factory.Notifications.Sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledNotification>();
        notification.Recipient.Email.ShouldBe(booking.Doctor.Email);
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByPatientTemplateKey);
        notification.RescheduledBy.ShouldBe(AppointmentParticipant.Patient);
        notification.PreviousDate.ShouldBe(Today().AddDays(DaysAhead));
        notification.PreviousStartTime.ShouldBe(new TimeOnly(AppointmentStartHour, 0));
        notification.Appointment.AppointmentId.ShouldBe(booking.AppointmentId);
        notification.Appointment.AppointmentDate.ShouldBe(Today().AddDays(NewDaysAhead));
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == booking.Patient.Email);
    }

    [Fact]
    public async Task El_horario_anterior_queda_libre_para_otro_paciente()
    {
        var startsAt = InTimeStart();
        var booking = await SeedBookingAsync(Confirmed, startsAt);
        var other = await SeedPatientAsync();
        using (var reschedule = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, NewStartLocal()))
        {
            await reschedule.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await RequestSlotAsync(other, booking.Doctor, startsAt);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Mover_la_cita_una_hora_mas_tarde_aunque_el_nuevo_horario_pise_el_anterior_se_permite()
    {
        var startsAt = InTimeStart();
        var booking = await SeedBookingAsync(Confirmed, startsAt);
        var later = startsAt.AddHours(HoursLater);

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, later);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadScheduleAsync(booking.AppointmentId)).ShouldBe(ScheduleAt(later));
    }

    [Fact]
    public async Task Reprogramar_fuera_de_plazo_se_rechaza_pide_llamar_al_medico_y_no_cambia_nada()
    {
        var startsAt = CurrentHourUtc().AddHours(HoursPastRescheduleDeadline);
        var booking = await SeedBookingAsync(Confirmed, startsAt);

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, NewStartLocal());

        var title = await AssertErrorAsync(response, HttpStatusCode.Conflict, RescheduleDeadlinePassedCode);
        title.ShouldContain(CallTheDoctorText);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_a_menos_de_la_antelacion_minima_es_TooSoon_y_la_cita_no_cambia()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());
        var tooSoon = Local(Today().AddDays(TooSoonDaysAhead), AppointmentStartHour);

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, tooSoon);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, TooSoonCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_que_no_esta_en_punto_es_NotOnHour()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());

        using var response = await RescheduleAsync(
            booking.Patient.Client, booking.AppointmentId, NewStartLocal().AddMinutes(HalfHourMinutes));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, NotOnHourCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task La_misma_hora_es_SameSchedule()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, booking.StartsAt);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, SameScheduleCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Sin_startsAt_responde_400_StartsAtRequired()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());

        using var response = await booking.Patient.Client.PostAsJsonAsync(UrlOf(booking.AppointmentId), new { });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, StartsAtRequiredCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_ocupada_por_otra_cita_del_medico_es_SlotUnavailable_y_no_avisa()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());
        var newStartsAt = NewStartLocal();
        await InsertAppointmentAsync(booking.Doctor, await SeedPatientAsync(), Confirmed, newStartsAt.ToUniversalTime());

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, newStartsAt);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, SlotUnavailableCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_sin_bloque_del_medico_es_SlotUnavailable()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart(), withNewBlock: false);

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, NewStartLocal());

        await AssertErrorAsync(response, HttpStatusCode.Conflict, SlotUnavailableCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_que_se_cruza_con_otra_cita_del_mismo_paciente_con_otro_medico_es_PatientOverlap()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());
        var newStartsAt = NewStartLocal();
        var otherDoctor = await SeedDoctorAsync();
        await InsertBlockAsync(
            otherDoctor.DoctorId,
            newStartsAt.ToUniversalTime().AddHours(-BlockMarginHours),
            newStartsAt.ToUniversalTime().AddHours(AppointmentHours + BlockMarginHours));
        await InsertAppointmentAsync(otherDoctor, booking.Patient, Confirmed, newStartsAt.ToUniversalTime());

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, newStartsAt);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, PatientOverlapCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Theory]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_en_estado_final_es_NotActive_y_la_fila_no_cambia(string status, string? statusReason)
    {
        var booking = await SeedBookingAsync(status, InTimeStart(), statusReason: statusReason);

        using var response = await RescheduleAsync(booking.Patient.Client, booking.AppointmentId, NewStartLocal());

        await AssertErrorAsync(response, HttpStatusCode.Conflict, NotActiveCode);
        (await ReadStateAsync(booking.AppointmentId)).ShouldBe(new AppointmentState(status, statusReason));
        (await ReadScheduleAsync(booking.AppointmentId)).ShouldBe(ScheduleAt(booking.StartsAt));
    }

    [Fact]
    public async Task Una_cita_de_otro_paciente_responde_404_y_no_cambia()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());
        var other = await SeedPatientAsync();

        using var response = await RescheduleAsync(other.Client, booking.AppointmentId, NewStartLocal());

        await AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Un_id_inexistente_responde_404()
    {
        var patient = await SeedPatientAsync();

        using var response = await RescheduleAsync(patient.Client, MissingAppointmentId, NewStartLocal());

        await AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
    }

    [Fact]
    public async Task Un_medico_autenticado_recibe_403()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());

        using var response = await RescheduleAsync(booking.Doctor.Client, booking.AppointmentId, NewStartLocal());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertUnchangedAsync(booking, Confirmed);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());
        using var anonymous = Factory.CreateClient();

        using var response = await RescheduleAsync(anonymous, booking.AppointmentId, NewStartLocal());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reprogramar_y_solicitar_a_la_vez_el_mismo_horario_deja_una_sola_cita_activa_y_un_409()
    {
        var booking = await SeedBookingAsync(Confirmed, InTimeStart());
        var requester = await SeedPatientAsync();
        var newStartsAt = NewStartLocal();

        var rescheduleTask = RescheduleAsync(booking.Patient.Client, booking.AppointmentId, newStartsAt);
        var requestTask = RequestSlotAsync(requester, booking.Doctor, newStartsAt.ToUniversalTime());
        await Task.WhenAll(rescheduleTask, requestTask);
        using var reschedule = await rescheduleTask;
        using var request = await requestTask;

        // Gana uno solo: la reprogramación responde 204 y la solicitud 201; la otra recibe el conflicto de horario.
        var rescheduleWon = reschedule.StatusCode == HttpStatusCode.NoContent;
        if (rescheduleWon)
        {
            await AssertErrorAsync(request, HttpStatusCode.Conflict, SlotUnavailableCode);
        }
        else
        {
            await request.ShouldHaveStatusAsync(HttpStatusCode.Created);
            await AssertErrorAsync(reschedule, HttpStatusCode.Conflict, SlotUnavailableCode);
        }

        (await CountActiveAppointmentsAsync(booking.Doctor.DoctorId, newStartsAt.ToUniversalTime())).ShouldBe(1);
    }

    /// <summary>Nueva hora de referencia: 12 días adelante a las 10:00 locales, con el desfase de Santo Domingo.</summary>
    private DateTimeOffset NewStartLocal() => Local(Today().AddDays(NewDaysAhead), AppointmentStartHour);

    private static Schedule ScheduleAt(DateTimeOffset startsAt) =>
        new(startsAt.ToUniversalTime(), startsAt.ToUniversalTime().AddHours(AppointmentHours));

    private static string UrlOf(int appointmentId) => $"{PatientAppointmentsUrl}/{appointmentId}/{RescheduleSuffix}";

    private static Task<HttpResponseMessage> RescheduleAsync(HttpClient client, int appointmentId, DateTimeOffset startsAt) =>
        client.PostAsJsonAsync(UrlOf(appointmentId), new { startsAt });

    private Task<HttpResponseMessage> RequestSlotAsync(PatientSeed patient, DoctorSeed doctor, DateTimeOffset startsAtUtc) =>
        patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = startsAtUtc,
            doctorId = doctor.DoctorId
        });

    /// <summary>
    /// Médico con el bloque de la cita y, si se pide, el del nuevo horario; y un paciente dueño de la cita en el estado
    /// indicado. El bloque nuevo cubre el horario con el mismo margen que el original.
    /// </summary>
    private async Task<Booking> SeedBookingAsync(
        string status, DateTimeOffset startsAtUtc, bool withNewBlock = true, string? statusReason = null)
    {
        var doctor = await SeedDoctorAsync();
        await InsertBlockAsync(
            doctor.DoctorId, startsAtUtc.AddHours(-BlockMarginHours), startsAtUtc.AddHours(AppointmentHours + BlockMarginHours));
        if (withNewBlock)
        {
            var newStartsAt = NewStartLocal().ToUniversalTime();
            await InsertBlockAsync(
                doctor.DoctorId, newStartsAt.AddHours(-BlockMarginHours), newStartsAt.AddHours(AppointmentHours + BlockMarginHours));
        }

        var patient = await SeedPatientAsync();
        var appointmentId = await InsertAppointmentAsync(doctor, patient, status, startsAtUtc, statusReason);

        return new Booking(doctor, patient, appointmentId, startsAtUtc);
    }

    private async Task<int> InsertAppointmentAsync(
        DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc, string? statusReason = null)
    {
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status, statusReason));

        return appointmentId;
    }

    private async Task<Schedule> ReadScheduleAsync(int appointmentId)
    {
        Schedule? schedule = null;
        await WithContextAsync(async context => schedule = await context.Database.SqlQuery<Schedule>(
            $"SELECT starts_at, ends_at FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

        return schedule!;
    }

    // Tras un rechazo la cita conserva su horario, su estado y no se avisó a nadie.
    private async Task AssertUnchangedAsync(Booking booking, string status)
    {
        (await ReadScheduleAsync(booking.AppointmentId)).ShouldBe(ScheduleAt(booking.StartsAt));
        (await ReadStateAsync(booking.AppointmentId)).ShouldBe(new AppointmentState(status, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    private async Task<int> CountActiveAppointmentsAsync(int doctorId, DateTimeOffset startsAtUtc)
    {
        var count = 0;
        await WithContextAsync(async context => count = await context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE doctor_id = {doctorId} AND status IN ('pending', 'confirmed') AND starts_at = {startsAtUtc}
            """).SingleAsync());

        return count;
    }
}
