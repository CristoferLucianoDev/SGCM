using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class FiltradoYOrdenDeReportesDeCitasSteps(ApiScenario scenario)
{
    private const string ReportsUrl = "/api/doctors/me/appointment-reports";
    private const string DateFormat = "yyyy-MM-dd";
    private const string RejectionReason = "Agenda completa";
    private const string FirstSpecialty = "primera";
    private const int AppointmentHours = 2;
    private const int AppointmentStartHour = 10;
    private const int SantoDomingoOffsetHours = -4;

    // Los valores del esquema se escriben en español; la API y la base los usan en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = "pending",
        ["confirmada"] = "confirmed",
        ["rechazada"] = "rejected",
        ["cancelada"] = "cancelled",
        ["completada"] = "completed"
    };

    private static readonly Dictionary<string, string> TypeBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["realizadas"] = "completed",
        ["canceladas"] = "cancelled",
        ["pendientes"] = "pending",
        ["todas"] = "all"
    };

    private static readonly Dictionary<string, string> SortFieldBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["fecha"] = "date",
        ["paciente"] = "patient",
        ["estado"] = "status"
    };

    private static readonly Dictionary<string, string> DirectionBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ascendente"] = "ascending",
        ["descendente"] = "descending"
    };

    private readonly Dictionary<string, int> _patientsByName = new(StringComparer.Ordinal);
    private int _doctorId;
    private int _firstSpecialtyId;
    private int _secondSpecialtyId;
    private AppointmentReportResponse? _report;

    [Given("que inicié sesión como médico para filtrar reportes de citas")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "filtrar-reportes-medico", id => userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _firstSpecialtyId = await AddSpecialtyAsync();
        _secondSpecialtyId = await AddSpecialtyAsync();
    }

    [Given("que para filtrar mis reportes tengo una cita {string} de {string} en mi {word} especialidad dentro de {int} día(s)")]
    public async Task DadoQueTengoUnaCita(string status, string patientName, string specialty, int days)
    {
        var patientId = await EnsurePatientAsync(patientName);
        var specialtyId = specialty == FirstSpecialty ? _firstSpecialtyId : _secondSpecialtyId;
        var startsAtUtc = new DateTimeOffset(
            TodayLocal().AddDays(days).ToDateTime(new TimeOnly(AppointmentStartHour, 0)), LocalOffset()).ToUniversalTime();
        var apiStatus = StatusBySpanishName[status];
        await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({_doctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentHours)}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, specialtyId, startsAtUtc, AppointmentHours, apiStatus,
                apiStatus == "rejected" ? RejectionReason : null);
        });
    }

    [When("filtro el reporte de citas {string} entre el día {int} y el día {int} con estado {string}, paciente {string} y mi segunda especialidad")]
    public Task CuandoFiltroConTodo(string type, int fromDays, int toDays, string status, string patientName) =>
        GetAsync($"{Criteria(type, fromDays, toDays)}&status={StatusBySpanishName[status]}" +
            $"&patientName={Uri.EscapeDataString(patientName)}&specialtyId={_secondSpecialtyId}");

    [When("filtro el reporte de citas {string} entre el día {int} y el día {int} con estado {string}")]
    public Task CuandoFiltroPorEstado(string type, int fromDays, int toDays, string status) =>
        GetAsync($"{Criteria(type, fromDays, toDays)}&status={StatusBySpanishName[status]}");

    [When("ordeno el reporte de citas {string} entre el día {int} y el día {int} por {string} en sentido {string}")]
    public Task CuandoOrdeno(string type, int fromDays, int toDays, string column, string direction) =>
        GetAsync($"{Criteria(type, fromDays, toDays)}&sortBy={SortFieldBySpanishName[column]}" +
            $"&sortDirection={DirectionBySpanishName[direction]}");

    [Then("el reporte filtrado tiene {int} fila(s)")]
    public void EntoncesElReporteTieneFilas(int count)
    {
        var report = Report();

        report.Items.Count.ShouldBe(count);
        report.TotalCount.ShouldBe(count);
    }

    [Then("la fila del reporte filtrado es de {string} del día {int} desde hoy con estado {string}")]
    public void EntoncesLaFilaEs(string patientName, int days, string status)
    {
        var row = Report().Items.ShouldHaveSingleItem();

        row.PatientName.ShouldBe(patientName);
        row.Date.ShouldBe(TodayLocal().AddDays(days));
        row.Status.ShouldBe(StatusBySpanishName[status]);
    }

    [Then("las filas del reporte filtrado son de los pacientes {string} en ese orden")]
    public void EntoncesLasFilasEstanEnOrden(string patients) =>
        Report().Items.Select(item => item.PatientName)
            .ShouldBe(patients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

    [Then("no hay filas en el registro de reportes")]
    public async Task EntoncesNoHayFilasEnElRegistro() =>
        (await WithContextAsync(async context => (await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM scheduling.appointment_report_log WHERE doctor_id = {_doctorId}")
            .ToListAsync()).Single())).ShouldBe(0);

    [Then("el reporte filtrado muestra el mensaje {string}")]
    public void EntoncesMuestraElMensaje(string message) => Report().Message.ShouldBe(message);

    private async Task GetAsync(string query)
    {
        scenario.Response?.Dispose();
        _report = null;
        scenario.Response = await scenario.Client.GetAsync($"{ReportsUrl}?{query}");
        await scenario.Response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        _report = await scenario.Response.Content.ReadFromJsonAsync<AppointmentReportResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private AppointmentReportResponse Report() =>
        _report.ShouldNotBeNull("No se consultó el reporte filtrado en este escenario.");

    private string Criteria(string type, int fromDays, int toDays) =>
        $"type={TypeBySpanishName[type]}&from={Format(TodayLocal().AddDays(fromDays))}" +
        $"&to={Format(TodayLocal().AddDays(toDays))}";

    // Un paciente por nombre: el primero que aparece se crea y se renombra.
    private async Task<int> EnsurePatientAsync(string fullName)
    {
        if (_patientsByName.TryGetValue(fullName, out var existing)) return existing;

        var parts = fullName.Split(' ', 2);
        var email = $"filtrar-reportes-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        var patientId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.patient SET first_name = {parts[0]}, last_name = {parts[1]} WHERE user_id = {user.Id}");
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });

        _patientsByName[fullName] = patientId;
        return patientId;
    }

    private Task<int> AddSpecialtyAsync() =>
        WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Hoy en Santo Domingo según el reloj de la aplicación; Santo Domingo está en UTC-4 todo el año.
    private DateOnly TodayLocal() =>
        DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(LocalOffset()).DateTime);

    private static TimeSpan LocalOffset() => TimeSpan.FromHours(SantoDomingoOffsetHours);

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
