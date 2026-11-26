using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos del vencimiento de citas no confirmadas. Siembra todo con <see cref="TestDataSeeder"/>, mueve el reloj
/// falso de la app compartida (que cada escenario reinicia) y ejecuta el comando como el proceso automático.
/// </summary>
[Binding]
public sealed class VencimientoDeCitasNoConfirmadasSteps(ApiScenario scenario)
{
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Cancelled = "cancelled";
    private const string TimeFormat = "HH:mm";
    private const int AppointmentHours = 2;
    private const int BlockMarginHours = 1;
    private const int SantoDomingoOffsetHours = -4;
    private const int NotificationsPerExpiredAppointment = 2;

    private sealed record Person(int Id, string Email);

    // Los estados del esquema se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = Pending,
        ["confirmada"] = Confirmed,
        ["cancelada"] = Cancelled
    };

    private Person? _patient;
    private Person? _doctor;
    private DateTimeOffset _startsAt;
    private int _appointmentId;
    private int _expired;

    [Given("que existe una solicitud {string} con su médico para dentro de {int} día(s) a las {string}")]
    public async Task DadoQueExisteUnaSolicitud(string spanishStatus, int days, string time)
    {
        var day = DateOnly.FromDateTime(
            scenario.Factory.Clock.UtcNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime).AddDays(days);
        _startsAt = new DateTimeOffset(
            day.ToDateTime(TimeOnly.ParseExact(time, TimeFormat, CultureInfo.InvariantCulture)),
            TimeSpan.FromHours(SantoDomingoOffsetHours)).ToUniversalTime();
        _patient = await SeedUserAsync(UserRole.Patient, "vencer-paciente");
        _doctor = await SeedUserAsync(UserRole.Doctor, "vencer-medico");
        var status = StatusBySpanishName[spanishStatus];
        var blockStart = _startsAt.AddHours(-BlockMarginHours);
        var blockEnd = _startsAt.AddHours(AppointmentHours + BlockMarginHours);
        _appointmentId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, _patient.Id);
            var patientId = await context.Patients.Where(patient => patient.UserId == _patient.Id)
                .Select(patient => patient.Id).SingleAsync();
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, _doctor.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {blockStart}, {blockEnd}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, _startsAt, AppointmentHours, status);
        });
    }

    [When("pasa el tiempo hasta {int} hora(s) antes del inicio de la cita")]
    public void CuandoPasaElTiempo(int hours) => scenario.Factory.Clock.UtcNow = _startsAt.AddHours(-hours);

    [When("se ejecuta el vencimiento de citas no confirmadas")]
    public async Task CuandoSeEjecutaElVencimiento()
    {
        // Mismo recorrido que el proceso automático: un scope propio y el manejador con sus decoradores.
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<ExpirePendingAppointmentsCommand, int>>();

        var result = await handler.HandleAsync(new ExpirePendingAppointmentsCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _expired = result.Value;
    }

    [Then("el vencimiento procesó {int} cita(s)")]
    public void EntoncesElVencimientoProceso(int count) => _expired.ShouldBe(count);

    [Then("el estado de la cita tras el vencimiento es {string}")]
    public async Task EntoncesElEstadoEs(string spanishStatus)
    {
        var status = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

        status.ShouldBe(StatusBySpanishName[spanishStatus]);
    }

    [Then("el motivo de cancelación de esa cita es {string}")]
    public async Task EntoncesElMotivoEs(string reason)
    {
        var saved = await WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status_reason AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

        saved.ShouldBe(reason);
        saved.ShouldBe(Appointment.ExpiredReason);
    }

    [Then("el paciente y el médico reciben el aviso de vencimiento de esa cita")]
    public void EntoncesLasDosPartesReciben()
    {
        var sent = scenario.Factory.Notifications.Sent;
        sent.Count.ShouldBe(NotificationsPerExpiredAppointment);
        var toPatient = sent.OfType<AppointmentExpiredNotification>()
            .Single(notification => notification.RecipientRole == AppointmentParticipant.Patient);
        var toDoctor = sent.OfType<AppointmentExpiredNotification>()
            .Single(notification => notification.RecipientRole == AppointmentParticipant.Doctor);

        toPatient.Recipient.Email.ShouldBe(_patient!.Email);
        toPatient.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForPatientTemplateKey);
        toPatient.Appointment.AppointmentId.ShouldBe(_appointmentId);
        toDoctor.Recipient.Email.ShouldBe(_doctor!.Email);
        toDoctor.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForDoctorTemplateKey);
        toDoctor.Appointment.AppointmentId.ShouldBe(_appointmentId);
    }

    [Then("no se envía ningún aviso de vencimiento")]
    public void EntoncesNoSeEnviaAviso() => scenario.Factory.Notifications.Sent.ShouldBeEmpty();

    private async Task<Person> SeedUserAsync(UserRole role, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(role, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);

        return new Person(user.Id, email);
    }

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
