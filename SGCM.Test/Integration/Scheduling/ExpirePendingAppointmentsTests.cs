using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración del vencimiento de citas pendientes no confirmadas, contra la base real: el comando se
/// resuelve con sus decoradores desde un scope, igual que lo hace el proceso automático, y el reloj de la app se
/// mueve para llegar al plazo. El reloj y las notificaciones los reinicia la base de pruebas en cada prueba.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExpirePendingAppointmentsTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const int ConfirmationDeadlineHours = 48;
    private const int HoursInsideDeadline = ConfirmationDeadlineHours - 1;
    private const int HoursAroundDeadline = 24;
    private const int ExpectedNotificationsPerExpiredAppointment = 2;

    [Fact]
    public async Task Con_el_reloj_a_47_horas_del_inicio_una_cita_pendiente_vence_y_avisa_al_paciente_y_al_medico()
    {
        var startsAt = InTimeStart();
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        MoveClockBefore(startsAt, HoursInsideDeadline);

        var expired = await ExpireAsync();

        expired.ShouldBe(1);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, Appointment.ExpiredReason));
        var sent = Factory.Notifications.Sent.OfType<AppointmentExpiredNotification>().ToList();
        Factory.Notifications.Sent.Count.ShouldBe(ExpectedNotificationsPerExpiredAppointment);
        var toPatient = sent.Single(notification => notification.RecipientRole == AppointmentParticipant.Patient);
        toPatient.Recipient.Email.ShouldBe(patient.Email);
        toPatient.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForPatientTemplateKey);
        toPatient.Appointment.AppointmentId.ShouldBe(appointmentId);
        var toDoctor = sent.Single(notification => notification.RecipientRole == AppointmentParticipant.Doctor);
        toDoctor.Recipient.Email.ShouldBe(doctor.Email);
        toDoctor.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForDoctorTemplateKey);
        toDoctor.ConfirmationDeadlineHours.ShouldBe(ConfirmationDeadlineHours);
    }

    [Fact]
    public async Task Con_el_reloj_a_48_horas_exactas_no_vence_ni_avisa()
    {
        var startsAt = InTimeStart();
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        MoveClockBefore(startsAt, ConfirmationDeadlineHours);

        var expired = await ExpireAsync();

        expired.ShouldBe(0);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_confirmada_dentro_de_las_48_horas_no_cambia()
    {
        var startsAt = InTimeStart();
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);
        MoveClockBefore(startsAt, HoursInsideDeadline);

        var expired = await ExpireAsync();

        expired.ShouldBe(0);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Rejected)]
    [InlineData(Cancelled)]
    [InlineData(Completed)]
    public async Task Las_citas_en_estado_final_dentro_de_la_ventana_no_cambian(string status)
    {
        var startsAt = InTimeStart();
        var reason = status == Completed ? null : RejectionReason;
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(status, startsAt, reason);
        MoveClockBefore(startsAt, HoursInsideDeadline);

        var expired = await ExpireAsync();

        expired.ShouldBe(0);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(status, reason));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Ejecutarlo_dos_veces_vence_la_cita_una_sola_vez_y_no_avisa_de_nuevo()
    {
        var startsAt = InTimeStart();
        await SeedOwnAppointmentAsync(Pending, startsAt);
        MoveClockBefore(startsAt, HoursInsideDeadline);

        var first = await ExpireAsync();
        var second = await ExpireAsync();

        first.ShouldBe(1);
        second.ShouldBe(0);
        Factory.Notifications.Sent.Count.ShouldBe(ExpectedNotificationsPerExpiredAppointment);
    }

    [Fact]
    public async Task El_horario_de_la_cita_vencida_queda_libre()
    {
        var startsAt = InTimeStart();
        var (doctor, _, _) = await SeedOwnAppointmentAsync(Pending, startsAt);
        MoveClockBefore(startsAt, HoursInsideDeadline);

        await ExpireAsync();

        (await CountActiveAppointmentsAsync(doctor.DoctorId, startsAt)).ShouldBe(0);
    }

    [Fact]
    public async Task Con_dos_citas_vencibles_y_una_todavia_en_plazo_solo_vencen_las_dos()
    {
        var startsAt = InTimeStart();
        var (_, _, firstId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var (_, _, secondId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var (_, _, laterId) = await SeedOwnAppointmentAsync(Pending, startsAt.AddHours(HoursAroundDeadline));
        MoveClockBefore(startsAt, HoursInsideDeadline);

        var expired = await ExpireAsync();

        expired.ShouldBe(2);
        (await ReadStateAsync(firstId)).ShouldBe(new AppointmentState(Cancelled, Appointment.ExpiredReason));
        (await ReadStateAsync(secondId)).ShouldBe(new AppointmentState(Cancelled, Appointment.ExpiredReason));
        (await ReadStateAsync(laterId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.Count.ShouldBe(2 * ExpectedNotificationsPerExpiredAppointment);
    }

    private void MoveClockBefore(DateTimeOffset startsAt, int hours) => Clock.UtcNow = startsAt.AddHours(-hours);

    /// <summary>
    /// Ejecuta el comando como el proceso automático: en un scope propio y con sus decoradores, que guardan en una
    /// transacción y publican los eventos después del commit.
    /// </summary>
    private async Task<int> ExpireAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ExpirePendingAppointmentsCommand, int>>();

        var result = await handler.HandleAsync(new ExpirePendingAppointmentsCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private async Task<int> CountActiveAppointmentsAsync(int doctorId, DateTimeOffset startsAt)
    {
        var endsAt = startsAt.AddHours(AppointmentHours);
        var count = 0;
        await WithContextAsync(async context => count = await context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE doctor_id = {doctorId}
              AND status IN ('pending', 'confirmed')
              AND starts_at < {endsAt}
              AND ends_at > {startsAt}
            """).SingleAsync());
        return count;
    }
}
