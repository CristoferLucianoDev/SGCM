using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de la matriz de notificaciones. El paciente usa el cliente del escenario y el médico uno propio; cada
/// transición se hace por la API (o con el proceso automático, en el vencimiento) y FakeNotificationService dice quién
/// recibió qué plantilla.
/// </summary>
[Binding]
public sealed class MatrizDeNotificacionesSteps(ApiScenario scenario) : IDisposable
{
    private const string Patient = "paciente";
    private const string Doctor = "médico";
    private const string OtherDoctor = "otro médico";
    private const int AppointmentHours = 2;
    private const int BlockStartHour = 8;
    private const int BlockEndHour = 16;
    private const int RescheduledHour = 13;
    private const string Reason = "Viaje al congreso";
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private readonly ApiScenario _doctor = new();
    private readonly Dictionary<string, string> _emails = [];
    private int _specialtyId;
    private int _doctorId;
    private int _blockId;
    private int _appointmentId;
    private DateOnly _day;
    private DateTimeOffset _startsAt;

    [Given("que un paciente y su médico tienen una cita {string} dentro de {int} días a las {string}")]
    public async Task DadoQueTienenUnaCita(string status, int days, string time)
    {
        var patientUserId = 0;
        var doctorUserId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Patient", "matriz-paciente", id => patientUserId = id);
        await AuthenticatedScenario.LoginAsync(_doctor, "Doctor", "matriz-medico", id => doctorUserId = id);
        _day = DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(SantoDomingo).DateTime).AddDays(days);
        _startsAt = Local(_day, TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture));

        await WithContextAsync(async context =>
        {
            _emails[Patient] = await EmailOfAsync(context, patientUserId);
            _emails[Doctor] = await EmailOfAsync(context, doctorUserId);
            _doctorId = await context.Database.SqlQuery<int>(
                $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {doctorUserId}").SingleAsync();
            _specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, _specialtyId);
            _blockId = await InsertBlockAsync(context, _doctorId);
            if (status == "ninguna") return 0;

            var patientId = await context.Patients.Where(value => value.UserId == patientUserId)
                .Select(value => value.Id).SingleAsync();
            _appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, _startsAt.ToUniversalTime(), AppointmentHours, status);
            return 0;
        });
        scenario.Factory.Notifications.Clear();
    }

    [When("ocurre {string}")]
    public async Task CuandoOcurre(string transition)
    {
        var rescheduledAt = Local(_day, new TimeOnly(RescheduledHour, 0));
        switch (transition)
        {
            case "la solicitud del paciente":
                await ExpectAsync(scenario, HttpStatusCode.Created, client => client.PostAsJsonAsync(
                    "/api/appointments", new { specialtyId = _specialtyId, startsAt = _startsAt, doctorId = _doctorId }));
                break;
            case "la solicitud con cambio de médico":
                await SeedOtherDoctorAndOccupyFirstAsync();
                await ExpectAsync(scenario, HttpStatusCode.Created, client => client.PostAsJsonAsync(
                    "/api/appointments", new { specialtyId = _specialtyId, startsAt = _startsAt, assignedDoctorId = _doctorId }));
                break;
            case "la confirmación del médico":
                await ExpectAsync(_doctor, HttpStatusCode.NoContent, client => client.PostAsync(DoctorUrl("confirm"), null));
                break;
            case "el rechazo del médico":
                await ExpectAsync(_doctor, HttpStatusCode.NoContent, client => client.PostAsJsonAsync(DoctorUrl("reject"), new { reason = Reason }));
                break;
            case "la cancelación del paciente":
                await ExpectAsync(scenario, HttpStatusCode.NoContent, client => client.PostAsJsonAsync(
                    $"/api/appointments/{_appointmentId}/cancel", new { reason = Reason }));
                break;
            case "la cancelación del médico":
                await ExpectAsync(_doctor, HttpStatusCode.NoContent, client => client.PostAsJsonAsync(DoctorUrl("cancel"), new { reason = Reason }));
                break;
            case "la reprogramación del paciente":
                await ExpectAsync(scenario, HttpStatusCode.NoContent, client => client.PostAsJsonAsync(
                    $"/api/patients/me/appointments/{_appointmentId}/reschedule", new { startsAt = rescheduledAt }));
                break;
            case "la reprogramación del médico":
                await ExpectAsync(_doctor, HttpStatusCode.NoContent, client => client.PostAsJsonAsync(
                    DoctorUrl("reschedule"), new { startsAt = rescheduledAt }));
                break;
            case "el vencimiento de la solicitud":
                await ExpireAsync();
                break;
            case "el cierre del bloque":
                await ExpectAsync(_doctor, HttpStatusCode.OK, client => client.PostAsJsonAsync(
                    $"/api/doctors/me/availability-blocks/{_blockId}/close", new { reason = Reason }));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(transition), transition, "Transición sin paso.");
        }
    }

    [Then("reciben correo exactamente {string}")]
    public void EntoncesRecibenCorreoExactamente(string expected)
    {
        var expectedEmails = expected.Split(',', StringSplitOptions.TrimEntries)
            .Select(item => item.Split(':', StringSplitOptions.TrimEntries))
            .Select(parts => $"{_emails[parts[0]]}:{parts[1]}")
            .ToArray();

        scenario.Factory.Notifications.Sent
            .Select(notification => $"{notification.Recipient.Email}:{notification.TemplateKey}")
            .ShouldBe(expectedEmails, ignoreOrder: true);
    }

    public void Dispose() => _doctor.Dispose();

    private string DoctorUrl(string action) => $"/api/doctors/me/appointments/{_appointmentId}/{action}";

    private static async Task ExpectAsync(
        ApiScenario actor, HttpStatusCode expected, Func<HttpClient, Task<HttpResponseMessage>> send)
    {
        using var response = await send(actor.Client);
        await response.ShouldHaveStatusAsync(expected);
    }

    // Mismo recorrido que el proceso automático: un scope propio y el manejador con sus decoradores, con el reloj ya
    // dentro del plazo de confirmación.
    private async Task ExpireAsync()
    {
        scenario.Factory.Clock.UtcNow = _startsAt.ToUniversalTime().AddHours(-1);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<ICommandHandler<ExpirePendingAppointmentsCommand, int>>()
            .HandleAsync(new ExpirePendingAppointmentsCommand(), CancellationToken.None);
        result.Value.ShouldBe(1);
    }

    // Otro médico de la misma especialidad con el horario libre, y el primero ocupado por la cita de otro paciente:
    // el sistema asigna al otro médico.
    private async Task SeedOtherDoctorAndOccupyFirstAsync()
    {
        var doctorEmail = $"matriz-otro-medico-{Guid.NewGuid():N}@correo.com";
        var doctorUser = UserMother.WithRole(UserRole.Doctor, email: Email.Create(doctorEmail).Value);
        await scenario.Factory.SaveAsync(doctorUser);
        var otherPatient = UserMother.WithRole(UserRole.Patient, email: Email.Create($"matriz-otro-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(otherPatient);
        _emails[OtherDoctor] = doctorEmail;

        await WithContextAsync(async context =>
        {
            var otherDoctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, doctorUser.Id);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, otherDoctorId, _specialtyId);
            await InsertBlockAsync(context, otherDoctorId);
            await TestDataSeeder.InsertPatientProfileAsync(context, otherPatient.Id);
            var otherPatientId = await context.Patients.Where(value => value.UserId == otherPatient.Id)
                .Select(value => value.Id).SingleAsync();
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, otherPatientId, _doctorId, _specialtyId, _startsAt.ToUniversalTime(), AppointmentHours, "pending");
        });
    }

    // Npgsql solo escribe timestamptz en UTC.
    private async Task<int> InsertBlockAsync(AppDbContext context, int doctorId)
    {
        var startsAt = Local(_day, new TimeOnly(BlockStartHour, 0)).ToUniversalTime();
        var endsAt = Local(_day, new TimeOnly(BlockEndHour, 0)).ToUniversalTime();
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAt}, {endsAt}, true)
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    private static async Task<string> EmailOfAsync(AppDbContext context, int userId) =>
        (await context.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync()).Value;

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static DateTimeOffset Local(DateOnly date, TimeOnly time) => new(date.ToDateTime(time), SantoDomingo);
}
