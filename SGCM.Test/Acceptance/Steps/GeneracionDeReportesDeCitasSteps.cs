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
public sealed class GeneracionDeReportesDeCitasSteps(ApiScenario scenario)
{
    private const string ReportsUrl = "/api/doctors/me/appointment-reports";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";
    private const string IdSuffix = "Id";
    private const string RejectionReason = "Agenda completa";
    private const int AppointmentHours = 2;
    private const int SantoDomingoOffsetHours = -4;

    private sealed record LogRow(string Action, string ReportType, int RowCount, string From, string To);

    // Los estados y tipos del esquema se escriben en español; la API y la base los usan en inglés.
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

    private int _doctorId;
    private int _specialtyId;
    private int? _patientId;
    private string? _rawResponse;
    private AppointmentReportResponse? _report;

    [Given("que inicié sesión como médico para generar reportes de citas")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "reportes-medico", id => userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _specialtyId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });
    }

    [Given("que para mis reportes tengo una cita {string} dentro de {int} día(s) a las {string}")]
    public async Task DadoQueTengoUnaCita(string status, int days, string time) =>
        await SeedAppointmentAsync(_doctorId, _specialtyId, status, StartsAtUtc(days, time));

    [Given("que otro médico tiene una cita {string} para sus reportes dentro de {int} día(s) a las {string}")]
    public async Task DadoQueOtroMedicoTieneUnaCita(string status, int days, string time)
    {
        var (doctorId, specialtyId) = await SeedOtherDoctorAsync();
        await SeedAppointmentAsync(doctorId, specialtyId, status, StartsAtUtc(days, time));
    }

    [When("genero el reporte de citas {string} entre el día {int} y el día {int} desde hoy")]
    public Task CuandoGeneroElReporte(string type, int fromDays, int toDays) =>
        PostAsync(new
        {
            type = TypeBySpanishName[type],
            from = Format(TodayLocal().AddDays(fromDays)),
            to = Format(TodayLocal().AddDays(toDays))
        });

    [When("genero el reporte de citas {string} sin rango de fechas")]
    public Task CuandoGeneroElReporteSinRango(string type) => PostAsync(new { type = TypeBySpanishName[type] });

    [Then("el reporte tiene {int} fila(s)")]
    public void EntoncesElReporteTieneFilas(int count)
    {
        var report = Report();

        report.Items.Count.ShouldBe(count);
        report.TotalCount.ShouldBe(count);
    }

    [Then("el reporte totaliza {int} cita(s) en estado {string}")]
    public void EntoncesElReporteTotaliza(int count, string status) =>
        Report().Totals.Single(total => total.Status == StatusBySpanishName[status]).Count.ShouldBe(count);

    [Then("la primera fila del reporte es del día {int} desde hoy a las {string} con el paciente {string}, la especialidad sembrada y el estado {string}")]
    public async Task EntoncesLaPrimeraFilaEs(int days, string time, string patientName, string status)
    {
        var first = Report().Items[0];

        first.Date.ShouldBe(TodayLocal().AddDays(days));
        first.StartTime.ShouldBe(ParseTime(time));
        first.PatientName.ShouldBe(patientName);
        first.SpecialtyName.ShouldBe(await SpecialtyNameAsync(_specialtyId));
        first.Status.ShouldBe(StatusBySpanishName[status]);
    }

    [Then("las filas del reporte solo tienen los estados {string} y {string}")]
    public void EntoncesLasFilasSoloTienenLosEstados(string first, string second) =>
        Report().Items.Select(item => item.Status).Distinct()
            .ShouldBe(new[] { StatusBySpanishName[first], StatusBySpanishName[second] }, ignoreOrder: true);

    [Then("la respuesta del reporte no contiene identificadores internos")]
    public void EntoncesNoContieneIdentificadores()
    {
        using var json = JsonDocument.Parse(_rawResponse.ShouldNotBeNull());

        PropertyNames(json.RootElement).ShouldAllBe(
            name => !name.Equals(IdSuffix, StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(IdSuffix, StringComparison.Ordinal));
    }

    [Then("queda registrada una generación de reporte de tipo {string} con {int} fila(s)")]
    public async Task EntoncesQuedaRegistrada(string type, int rowCount)
    {
        var log = (await ReadLogsAsync()).ShouldHaveSingleItem();

        log.Action.ShouldBe("generate");
        log.ReportType.ShouldBe(TypeBySpanishName[type]);
        log.RowCount.ShouldBe(rowCount);
    }

    [Then("el registro del reporte guarda el rango del día {int} al día {int} desde hoy")]
    public async Task EntoncesElRegistroGuardaElRango(int fromDays, int toDays)
    {
        var log = (await ReadLogsAsync()).ShouldHaveSingleItem();

        log.From.ShouldBe(Format(TodayLocal().AddDays(fromDays)));
        log.To.ShouldBe(Format(TodayLocal().AddDays(toDays)));
    }

    [Then("el reporte muestra el mensaje {string}")]
    public void EntoncesElReporteMuestraElMensaje(string message) => Report().Message.ShouldBe(message);

    [Then("todos los totales del reporte están en cero")]
    public void EntoncesTodosLosTotalesEnCero()
    {
        var totals = Report().Totals;

        totals.Count.ShouldBe(StatusBySpanishName.Count);
        totals.ShouldAllBe(total => total.Count == 0);
    }

    [Then("la generación del reporte se rechaza con el código {string}")]
    public void EntoncesLaGeneracionSeRechaza(string code)
    {
        ((int)scenario.Response!.StatusCode).ShouldBe((int)HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(_rawResponse.ShouldNotBeNull());
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    private async Task PostAsync(object body)
    {
        scenario.Response?.Dispose();
        _report = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync(ReportsUrl, body);
        _rawResponse = await scenario.Response.Content.ReadAsStringAsync();
        if (scenario.Response.StatusCode != HttpStatusCode.OK) return;

        _report = JsonSerializer.Deserialize<AppointmentReportResponse>(
            _rawResponse, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private AppointmentReportResponse Report() =>
        _report.ShouldNotBeNull("No se generó un reporte en este escenario.");

    /// <summary>
    /// Cita de un paciente del escenario en el estado pedido, dentro de un bloque que la cubre exactamente. Pasa por
    /// transiciones válidas hasta el estado final.
    /// </summary>
    private async Task SeedAppointmentAsync(int doctorId, int specialtyId, string spanishStatus, DateTimeOffset startsAtUtc)
    {
        var status = StatusBySpanishName[spanishStatus];
        var patientId = await EnsurePatientAsync();
        await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentHours)}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, startsAtUtc, AppointmentHours, status,
                status == "rejected" ? RejectionReason : null);
        });
    }

    // Un solo paciente por escenario, con el nombre que siembra TestDataSeeder.
    private async Task<int> EnsurePatientAsync()
    {
        if (_patientId is { } existing) return existing;

        var email = $"reportes-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        _patientId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });

        return _patientId.Value;
    }

    private async Task<(int DoctorId, int SpecialtyId)> SeedOtherDoctorAsync()
    {
        var email = $"reportes-medico-ajeno-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        return await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
    }

    private Task<List<LogRow>> ReadLogsAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<LogRow>(
            $"""
            SELECT action, report_type, row_count, filters ->> 'from' AS "from", filters ->> 'to' AS "to"
            FROM scheduling.appointment_report_log
            WHERE doctor_id = {_doctorId}
            """).ToListAsync());

    private Task<string> SpecialtyNameAsync(int specialtyId) =>
        WithContextAsync(context => context.Specialties.Where(specialty => specialty.Id == specialtyId)
            .Select(specialty => specialty.Name).SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(property => PropertyNames(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => []
    };

    private DateTimeOffset StartsAtUtc(int days, string time) =>
        new DateTimeOffset(TodayLocal().AddDays(days).ToDateTime(ParseTime(time)), LocalOffset()).ToUniversalTime();

    // Hoy en Santo Domingo según el reloj de la aplicación; Santo Domingo está en UTC-4 todo el año.
    private DateOnly TodayLocal() =>
        DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(LocalOffset()).DateTime);

    private static TimeSpan LocalOffset() => TimeSpan.FromHours(SantoDomingoOffsetHours);

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
