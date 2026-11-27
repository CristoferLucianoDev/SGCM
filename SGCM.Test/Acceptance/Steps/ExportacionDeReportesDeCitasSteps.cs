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
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ExportacionDeReportesDeCitasSteps(ApiScenario scenario)
{
    private const string ExportUrl = "/api/doctors/me/appointment-reports/export";
    private const string DateFormat = "yyyy-MM-dd";
    private const string PatientColumn = "Paciente";
    private const string IdSuffix = "Id";
    private const int AppointmentHours = 2;
    private const int AppointmentStartHour = 10;
    private const int SantoDomingoOffsetHours = -4;

    // Los valores del esquema se escriben en español; la API y la base los usan en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = "pending",
        ["confirmada"] = "confirmed",
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

    private static readonly Dictionary<string, string> ContentTypeByFormat = new(StringComparer.Ordinal)
    {
        ["pdf"] = ExportedReportFiles.PdfContentType,
        ["xlsx"] = ExportedReportFiles.XlsxContentType,
        ["docx"] = ExportedReportFiles.DocxContentType
    };

    private sealed record LogRow(string Action, string? ExportFormat, int RowCount);

    private int _doctorId;
    private int _specialtyId;
    private string? _format;
    private byte[]? _file;
    private string? _rawResponse;

    [Given("que inicié sesión como médico para exportar reportes de citas")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "exportar-reportes-medico", id => userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _specialtyId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });
    }

    [Given("que para exportar mis reportes tengo una cita {string} de {string} dentro de {int} día(s)")]
    public async Task DadoQueTengoUnaCita(string status, string patientName, int days)
    {
        var patientId = await SeedPatientAsync(patientName);
        var startsAtUtc = new DateTimeOffset(
            TodayLocal().AddDays(days).ToDateTime(new TimeOnly(AppointmentStartHour, 0)), LocalOffset()).ToUniversalTime();
        await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({_doctorId}, NULL, {startsAtUtc}, {startsAtUtc.AddHours(AppointmentHours)}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, startsAtUtc, AppointmentHours, StatusBySpanishName[status]);
        });
    }

    [When("exporto en {string} el reporte de citas {string} entre el día {int} y el día {int} ordenado por paciente descendente")]
    public async Task CuandoExporto(string format, string type, int fromDays, int toDays)
    {
        _format = format;
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.PostAsJsonAsync(ExportUrl, new
        {
            format,
            type = TypeBySpanishName[type],
            from = Format(TodayLocal().AddDays(fromDays)),
            to = Format(TodayLocal().AddDays(toDays)),
            sortBy = "patient",
            sortDirection = "descending"
        });
        if (scenario.Response.StatusCode == HttpStatusCode.OK)
            _file = await scenario.Response.Content.ReadAsByteArrayAsync();
        else
            _rawResponse = await scenario.Response.Content.ReadAsStringAsync();
    }

    [Then("recibo un archivo {string} válido")]
    public void EntoncesReciboUnArchivoValido(string format)
    {
        scenario.Response!.Content.Headers.ContentType!.MediaType.ShouldBe(ContentTypeByFormat[format]);
        var table = Table();
        if (table is null) ExportedReportFiles.PdfPageCount(File()).ShouldBeGreaterThanOrEqualTo(1);
        else table.Count.ShouldBeGreaterThan(1);
    }

    [Then("queda registrada una exportación en formato {string} con {int} fila(s)")]
    public async Task EntoncesQuedaRegistrada(string format, int rowCount)
    {
        var log = (await ReadLogsAsync()).ShouldHaveSingleItem();

        log.Action.ShouldBe("export");
        log.ExportFormat.ShouldBe(format);
        log.RowCount.ShouldBe(rowCount);
    }

    [Then("la tabla del archivo exportado es de los pacientes {string} en ese orden")]
    public void EntoncesLaTablaEsDeLosPacientes(string patients)
    {
        var table = Table().ShouldNotBeNull("El formato exportado no tiene una tabla legible en las pruebas.");
        var patientColumn = table[0].ToList().IndexOf(PatientColumn);

        table.Skip(1).Select(row => row[patientColumn])
            .ShouldBe(patients.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
    }

    [Then("la tabla del archivo exportado no tiene identificadores internos")]
    public void EntoncesNoTieneIdentificadores() =>
        Table().ShouldNotBeNull()[0].ShouldAllBe(header => !header.EndsWith(IdSuffix, StringComparison.Ordinal));

    [Then("la exportación se rechaza con el código {string} y un mensaje")]
    public void EntoncesLaExportacionSeRechaza(string code)
    {
        ((int)scenario.Response!.StatusCode).ShouldBe((int)HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(_rawResponse.ShouldNotBeNull());
        json.RootElement.GetProperty("code").GetString().ShouldBe(code);
        json.RootElement.GetProperty("title").GetString().ShouldNotBeNullOrWhiteSpace();
    }

    [Then("no queda ninguna exportación registrada")]
    public async Task EntoncesNoQuedaRegistrada() => (await ReadLogsAsync()).ShouldBeEmpty();

    private byte[] File() => _file.ShouldNotBeNull("No se exportó un archivo en este escenario.");

    // El PDF no tiene una tabla que se pueda leer en las pruebas; Excel y Word sí.
    private List<IReadOnlyList<string>>? Table() => _format switch
    {
        "xlsx" => ExportedReportFiles.ReadExcelTable(File()),
        "docx" => ExportedReportFiles.ReadWordTable(File()),
        _ => null
    };

    private async Task<int> SeedPatientAsync(string fullName)
    {
        var parts = fullName.Split(' ', 2);
        var email = $"exportar-reportes-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        return await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.patient SET first_name = {parts[0]}, last_name = {parts[1]} WHERE user_id = {user.Id}");
            return await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });
    }

    private Task<List<LogRow>> ReadLogsAsync() =>
        WithContextAsync(context => context.Database.SqlQuery<LogRow>(
            $"""
            SELECT action, export_format, row_count
            FROM scheduling.appointment_report_log
            WHERE doctor_id = {_doctorId}
            """).ToListAsync());

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
