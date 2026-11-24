using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Siembra y utilidades que comparten las pruebas de integración de la cancelación de citas, tanto la del
/// paciente como la del médico: médicos y pacientes con sesión iniciada, bloques, citas en cualquier estado y las
/// verificaciones de errores de la API.
/// </summary>
public abstract class AppointmentCancellationTestBase(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    protected const string Password = "Secreta123";
    protected const string AppointmentsUrl = "/api/appointments";
    protected const string Pending = "pending";
    protected const string Confirmed = "confirmed";
    protected const string Rejected = "rejected";
    protected const string Cancelled = "cancelled";
    protected const string Completed = "completed";
    protected const string RejectionReason = "Agenda completa";
    protected const string EarlierCancellationReason = "Cancelada antes";
    protected const int AppointmentHours = 2;
    protected const int DaysAhead = 10;
    protected const int AppointmentStartHour = 10;
    protected const int HoursPastTheDeadline = 23;
    protected const int BlockMarginHours = 1;
    protected const int SantoDomingoOffsetHours = -4;

    protected sealed record DoctorSeed(string Email, int DoctorId, int SpecialtyId, HttpClient Client);

    protected sealed record PatientSeed(string Email, int PatientId, HttpClient Client);

    protected sealed record AppointmentState(string Status, string? StatusReason);

    private readonly List<HttpClient> _clients = [];

    // El reloj falso arranca en una fecha fija; los tokens vencen según el reloj real y el plazo se calcula con el
    // reloj de la app, así que las pruebas trabajan con la hora actual desde el principio.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow = DateTimeOffset.UtcNow;
    }

    public override async Task DisposeAsync()
    {
        foreach (var client in _clients) client.Dispose();
        await base.DisposeAsync();
    }

    /// <summary>Inicio en punto, 10 días adelante, dentro del plazo para cancelar.</summary>
    protected DateTimeOffset InTimeStart() =>
        Local(Today().AddDays(DaysAhead), AppointmentStartHour).ToUniversalTime();

    /// <summary>Hora en punto actual más 23 horas: siempre queda a menos de 24 horas, fuera del plazo.</summary>
    protected DateTimeOffset PastDeadlineStart() => CurrentHourUtc().AddHours(HoursPastTheDeadline);

    /// <summary>Hora en punto actual en UTC, truncada.</summary>
    protected DateTimeOffset CurrentHourUtc()
    {
        var now = Clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
    }

    /// <summary>Médico con un bloque que contiene la cita y un paciente dueño de una cita en el estado indicado.</summary>
    protected async Task<(DoctorSeed Doctor, PatientSeed Patient, int AppointmentId)> SeedOwnAppointmentAsync(
        string status, DateTimeOffset startsAtUtc, string? statusReason = null)
    {
        var doctor = await SeedDoctorAsync();
        await InsertBlockAsync(
            doctor.DoctorId, startsAtUtc.AddHours(-BlockMarginHours), startsAtUtc.AddHours(AppointmentHours + BlockMarginHours));
        var patient = await SeedPatientAsync();
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status, statusReason));

        return (doctor, patient, appointmentId);
    }

    protected async Task<AppointmentState> ReadStateAsync(int appointmentId)
    {
        AppointmentState? state = null;
        await WithContextAsync(async context => state = await context.Database.SqlQuery<AppointmentState>(
            $"""
            SELECT status, status_reason
            FROM scheduling.appointment
            WHERE id = {appointmentId}
            """).SingleAsync());
        return state!;
    }

    /// <summary>Crea un médico activo con perfil completo, una especialidad nueva y un cliente propio con su sesión iniciada.</summary>
    protected async Task<DoctorSeed> SeedDoctorAsync()
    {
        var email = $"medico-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        var doctorId = 0;
        var specialtyId = 0;
        await WithContextAsync(async context =>
        {
            doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
        });

        return new DoctorSeed(email, doctorId, specialtyId, await CreateLoggedInClientAsync(email));
    }

    /// <summary>Crea un paciente con perfil completo y un cliente propio con su sesión iniciada.</summary>
    protected async Task<PatientSeed> SeedPatientAsync()
    {
        var email = $"paciente-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Patient, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        var patientId = 0;
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            patientId = await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });

        return new PatientSeed(email, patientId, await CreateLoggedInClientAsync(email));
    }

    // Npgsql solo escribe timestamptz en UTC, por eso los instantes llegan ya en UTC.
    protected Task InsertBlockAsync(int doctorId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc) =>
        WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAtUtc}, {endsAtUtc}, true)
            """));

    /// <summary>Verifica el status y el código del ProblemDetails, y devuelve su título ya sin escapes de JSON.</summary>
    protected static async Task<string> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.ShouldHaveStatusAsync(status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem.GetProperty("title").GetString()!;
    }

    // Hoy en Santo Domingo según el reloj falso de la aplicación; Santo Domingo está en UTC-4 todo el año.
    protected DateOnly Today() =>
        DateOnly.FromDateTime(Clock.UtcNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    protected static DateTimeOffset Local(DateOnly day, int hour) =>
        new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(SantoDomingoOffsetHours));

    // private protected porque AppDbContext es internal y la clase base es pública.
    private protected async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Cliente propio, registrado para liberarse al terminar la prueba, con la sesión del usuario iniciada.</summary>
    private async Task<HttpClient> CreateLoggedInClientAsync(string email)
    {
        var client = Factory.CreateClient();
        _clients.Add(client);
        using var login = await LoginAsync(email, Password, client);
        await login.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var tokens = await login.ReadLoginAsync();
        tokens.PendingStep.ShouldBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return client;
    }
}
