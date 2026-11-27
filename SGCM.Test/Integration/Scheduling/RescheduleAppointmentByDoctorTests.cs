using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración de la reprogramación de una cita por el médico, contra la API y la base reales: el cambio de
/// horario y de estado, el aviso al paciente con la plantilla real, la liberación del horario anterior, las reglas de
/// hora futura, de cita ya empezada y de estado, el horario libre y la propiedad de la cita. Todo va por HTTP, así que
/// el reloj no se mueve y las citas se siembran relativas a la hora actual.
/// </summary>
[Trait("Category", "Integration")]
public sealed class RescheduleAppointmentByDoctorTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string RescheduleSuffix = "reschedule";
    private const string NewStartNotInFutureCode = "Appointment.NewStartNotInFuture";
    private const string SameScheduleCode = "Appointment.SameSchedule";
    private const string NotOnHourCode = "Appointment.NotOnHour";
    private const string StartsAtRequiredCode = "Appointment.StartsAtRequired";
    private const string AlreadyStartedCode = "Appointment.AlreadyStarted";
    private const string SlotUnavailableCode = "Appointment.SlotUnavailable";
    private const string PatientOverlapCode = "Appointment.PatientOverlap";
    private const string NotActiveCode = "Appointment.NotActive";
    private const string NotFoundCode = "Appointment.NotFound";
    private const string ConfirmedInNewScheduleText = "La cita queda confirmada en el nuevo horario.";
    private const int TomorrowHours = 26;
    private const int HoursUntilStartInsideTwoDays = 23;
    private const int HoursUntilNewStartInsideTwoDays = 30;
    private const int HoursSinceStartWhenStarted = 1;
    private const int HoursBeforeNowForPastStart = 1;
    private const int HalfHourMinutes = 30;
    private const int MissingAppointmentId = int.MaxValue;

    private sealed record Schedule(DateTimeOffset StartsAt, DateTimeOffset EndsAt);

    [Theory]
    [InlineData(Pending)]
    [InlineData(Confirmed)]
    public async Task Mover_la_cita_propia_a_una_hora_libre_de_manana_la_deja_confirmada_y_avisa_al_paciente(string status)
    {
        var startsAt = InTimeStart();
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(status, startsAt);
        var newStartsAt = Tomorrow();
        await InsertBlockAroundAsync(doctor, newStartsAt);

        using var response = await RescheduleAsync(doctor.Client, appointmentId, newStartsAt);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadScheduleAsync(appointmentId)).ShouldBe(ScheduleAt(newStartsAt));
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
        var notification = Factory.Notifications.Sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledNotification>();
        notification.Recipient.Email.ShouldBe(patient.Email);
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByDoctorTemplateKey);
        notification.RescheduledBy.ShouldBe(AppointmentParticipant.Doctor);
        notification.PreviousDate.ShouldBe(Today().AddDays(DaysAhead));
        notification.PreviousStartTime.ShouldBe(new TimeOnly(AppointmentStartHour, 0));
        notification.Appointment.AppointmentId.ShouldBe(appointmentId);
        notification.Appointment.AppointmentDate.ShouldBe(LocalDateOf(newStartsAt));
        notification.Appointment.StartTime.ShouldBe(LocalTimeOf(newStartsAt));
        AssertRenderedBody(notification);
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == doctor.Email);
    }

    [Fact]
    public async Task Con_menos_de_48_horas_para_el_inicio_el_medico_tambien_puede_moverla()
    {
        var startsAt = CurrentHourUtc().AddHours(HoursUntilStartInsideTwoDays);
        var newStartsAt = CurrentHourUtc().AddHours(HoursUntilNewStartInsideTwoDays);
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        await InsertBlockAroundAsync(doctor, newStartsAt);

        using var response = await RescheduleAsync(doctor.Client, appointmentId, newStartsAt);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadScheduleAsync(appointmentId)).ShouldBe(ScheduleAt(newStartsAt));
    }

    [Fact]
    public async Task El_horario_anterior_queda_libre_para_un_paciente()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        await InsertBlockAroundAsync(doctor, Tomorrow());
        var other = await SeedPatientAsync();
        using (var reschedule = await RescheduleAsync(doctor.Client, appointmentId, Tomorrow()))
        {
            await reschedule.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await RequestSlotAsync(other, doctor, startsAt);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Una_nueva_hora_pasada_es_NewStartNotInFuture_y_la_cita_no_cambia()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await RescheduleAsync(
            doctor.Client, appointmentId, CurrentHourUtc().AddHours(-HoursBeforeNowForPastStart));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, NewStartNotInFutureCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task La_misma_hora_es_SameSchedule()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await RescheduleAsync(doctor.Client, appointmentId, startsAt);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, SameScheduleCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_que_no_esta_en_punto_es_NotOnHour()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await RescheduleAsync(
            doctor.Client, appointmentId, Tomorrow().AddMinutes(HalfHourMinutes));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, NotOnHourCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Sin_startsAt_responde_400_StartsAtRequired()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await doctor.Client.PostAsJsonAsync(UrlOf(appointmentId), new { });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, StartsAtRequiredCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Una_cita_que_ya_empezo_es_AlreadyStarted_y_no_cambia()
    {
        var startsAt = CurrentHourUtc().AddHours(-HoursSinceStartWhenStarted);
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        await InsertBlockAroundAsync(doctor, Tomorrow());

        using var response = await RescheduleAsync(doctor.Client, appointmentId, Tomorrow());

        await AssertErrorAsync(response, HttpStatusCode.Conflict, AlreadyStartedCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_ocupada_por_otra_cita_del_medico_es_SlotUnavailable_y_no_avisa()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        var newStartsAt = Tomorrow();
        await InsertBlockAroundAsync(doctor, newStartsAt);
        await InsertAppointmentAsync(doctor, await SeedPatientAsync(), Confirmed, newStartsAt);

        using var response = await RescheduleAsync(doctor.Client, appointmentId, newStartsAt);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, SlotUnavailableCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_sin_bloque_del_medico_es_SlotUnavailable_y_no_avisa()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await RescheduleAsync(doctor.Client, appointmentId, Tomorrow());

        await AssertErrorAsync(response, HttpStatusCode.Conflict, SlotUnavailableCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Una_nueva_hora_que_se_cruza_con_otra_cita_del_paciente_con_otro_medico_es_PatientOverlap()
    {
        var startsAt = InTimeStart();
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        var newStartsAt = Tomorrow();
        await InsertBlockAroundAsync(doctor, newStartsAt);
        var otherDoctor = await SeedDoctorAsync();
        await InsertBlockAroundAsync(otherDoctor, newStartsAt);
        await InsertAppointmentAsync(otherDoctor, patient, Confirmed, newStartsAt);

        using var response = await RescheduleAsync(doctor.Client, appointmentId, newStartsAt);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, PatientOverlapCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Theory]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_en_estado_final_es_NotActive_y_la_fila_no_cambia(string status, string? statusReason)
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(status, startsAt, statusReason);
        await InsertBlockAroundAsync(doctor, Tomorrow());

        using var response = await RescheduleAsync(doctor.Client, appointmentId, Tomorrow());

        await AssertErrorAsync(response, HttpStatusCode.Conflict, NotActiveCode);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(status, statusReason));
        (await ReadScheduleAsync(appointmentId)).ShouldBe(ScheduleAt(startsAt));
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_404_y_no_cambia()
    {
        var startsAt = InTimeStart();
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        var otherDoctor = await SeedDoctorAsync();
        await InsertBlockAroundAsync(otherDoctor, Tomorrow());

        using var response = await RescheduleAsync(otherDoctor.Client, appointmentId, Tomorrow());

        await AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Un_id_inexistente_responde_404()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await RescheduleAsync(doctor.Client, MissingAppointmentId, Tomorrow());

        await AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
    }

    [Fact]
    public async Task Un_paciente_autenticado_recibe_403()
    {
        var startsAt = InTimeStart();
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await RescheduleAsync(patient.Client, appointmentId, Tomorrow());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await AssertUnchangedAsync(appointmentId, startsAt, Confirmed);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        using var anonymous = Factory.CreateClient();

        using var response = await RescheduleAsync(anonymous, appointmentId, Tomorrow());

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    /// <summary>Misma hora de mañana: 26 horas después de la hora en punto actual, en punto y siempre futura.</summary>
    private DateTimeOffset Tomorrow() => CurrentHourUtc().AddHours(TomorrowHours);

    private static string UrlOf(int appointmentId) => $"{DoctorAppointmentsUrl}/{appointmentId}/{RescheduleSuffix}";

    private static Task<HttpResponseMessage> RescheduleAsync(HttpClient client, int appointmentId, DateTimeOffset startsAt) =>
        client.PostAsJsonAsync(UrlOf(appointmentId), new { startsAt });

    private Task<HttpResponseMessage> RequestSlotAsync(PatientSeed patient, DoctorSeed doctor, DateTimeOffset startsAtUtc) =>
        patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = startsAtUtc,
            doctorId = doctor.DoctorId
        });

    private static Schedule ScheduleAt(DateTimeOffset startsAt) =>
        new(startsAt.ToUniversalTime(), startsAt.ToUniversalTime().AddHours(AppointmentHours));

    // Santo Domingo está en UTC-4 todo el año.
    private static DateOnly LocalDateOf(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    private static TimeOnly LocalTimeOf(DateTimeOffset instant) =>
        TimeOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    /// <summary>Bloque abierto que contiene una cita en ese inicio, con el mismo margen que los de la siembra base.</summary>
    private Task InsertBlockAroundAsync(DoctorSeed doctor, DateTimeOffset startsAtUtc) =>
        InsertBlockAsync(
            doctor.DoctorId, startsAtUtc.AddHours(-BlockMarginHours), startsAtUtc.AddHours(AppointmentHours + BlockMarginHours));

    private async Task<int> InsertAppointmentAsync(
        DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc)
    {
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status));

        return appointmentId;
    }

    private async Task<Schedule> ReadScheduleAsync(int appointmentId)
    {
        Schedule? schedule = null;
        await WithContextAsync(async context => schedule = await context.Database.SqlQuery<Schedule>(
            $"SELECT starts_at, ends_at FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

        return schedule!;
    }

    // Tras un rechazo la cita conserva su horario y su estado, y no se avisó a nadie.
    private async Task AssertUnchangedAsync(int appointmentId, DateTimeOffset startsAtUtc, string status)
    {
        (await ReadScheduleAsync(appointmentId)).ShouldBe(ScheduleAt(startsAtUtc));
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(status, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    // Renderiza el cuerpo con la plantilla real, igual que el servicio de correo, y comprueba el texto del médico.
    private static void AssertRenderedBody(AppointmentRescheduledNotification notification)
    {
        var template = new NotificationTemplateStore().Get(notification.TemplateKey);

        var body = new TemplateRenderer().Render(template.HtmlBody, notification.GetValues(), htmlEncode: true);

        body.ShouldContain(ConfirmedInNewScheduleText);
        TemplatePlaceholders.Extract(body).ShouldBeEmpty();
    }
}
