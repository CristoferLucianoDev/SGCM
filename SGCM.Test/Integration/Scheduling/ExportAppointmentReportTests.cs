using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración de la exportación de reportes contra la API real: cada formato genera un archivo válido con
/// las mismas filas y en el mismo orden que la pantalla, cada exportación queda en el registro de reportes con su
/// formato y el reporte vacío se rechaza.
/// </summary>
[Trait("Category", "Integration")]
public sealed class ExportAppointmentReportTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string ReportsUrl = "/api/doctors/me/appointment-reports";
    private const string ExportUrl = "/api/doctors/me/appointment-reports/export";
    private const string DateFormat = "yyyy-MM-dd";
    private const string FileDateFormat = "dd/MM/yyyy";
    private const string FileTimeFormat = "HH:mm";
    private const string ReasonForVisit = "Control anual";
    private const string EmptyReportCode = "Report.EmptyReport";
    private const int FirstDay = 1;
    private const int LastDay = 20;
    private const int SortedQueryPageSize = 100;

    private static readonly Dictionary<string, string> StatusNames = new(StringComparer.Ordinal)
    {
        [Pending] = "Pendiente",
        [Confirmed] = "Confirmada",
        [Rejected] = "Rechazada",
        [Cancelled] = "Cancelada",
        [Completed] = "Completada"
    };

    private static readonly string[] Columns = ["Fecha", "Hora", "Paciente", "Especialidad", "Estado", "Motivo de consulta"];

    private sealed record LogRow(string Action, string ReportType, string? ExportFormat, int RowCount, bool FiltersMatch);

    [Theory]
    [InlineData("xlsx")]
    [InlineData("docx")]
    public async Task El_archivo_tiene_las_mismas_filas_en_el_mismo_orden_que_la_pantalla(string format)
    {
        var doctor = await SeedDoctorAsync();
        await SeedVariedAppointmentsAsync(doctor);
        var criteria = Criteria("all", sortBy: "patient", sortDirection: "descending");

        var screen = await GetScreenAsync(doctor, criteria);
        var file = await ExportAsync(doctor, criteria, format);

        var table = format == "xlsx"
            ? ExportedReportFiles.ReadExcelTable(file)
            : ExportedReportFiles.ReadWordTable(file);
        table[0].ShouldBe(Columns);
        table.Skip(1).ShouldBe(screen.Items.Select(ToFileRow));
    }

    [Fact]
    public async Task El_pdf_es_un_archivo_valido()
    {
        var doctor = await SeedDoctorAsync();
        await SeedVariedAppointmentsAsync(doctor);

        var file = await ExportAsync(doctor, Criteria("all"), "pdf");

        ExportedReportFiles.PdfPageCount(file).ShouldBeGreaterThanOrEqualTo(1);
    }

    [Theory]
    [InlineData("pdf", ExportedReportFiles.PdfContentType)]
    [InlineData("xlsx", ExportedReportFiles.XlsxContentType)]
    [InlineData("docx", ExportedReportFiles.DocxContentType)]
    public async Task Responde_el_archivo_con_su_tipo_y_nombre_de_descarga(string format, string contentType)
    {
        var doctor = await SeedDoctorAsync();
        await SeedVariedAppointmentsAsync(doctor);

        using var response = await doctor.Client.PostAsJsonAsync(ExportUrl, Body(Criteria("all"), format));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe(contentType);
        var fileName = response.Content.Headers.ContentDisposition!.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName;
        fileName.ShouldNotBeNull().Trim('"').ShouldMatch($@"^reporte-citas-\d{{8}}-\d{{4}}\.{format}$");
    }

    [Fact]
    public async Task Solo_exporta_las_citas_del_medico_autenticado()
    {
        var doctor = await SeedDoctorAsync();
        var otherDoctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAsync(doctor, patient, Pending, FirstDay);
        await SeedAsync(otherDoctor, patient, Pending, FirstDay + 1);

        var table = ExportedReportFiles.ReadExcelTable(await ExportAsync(doctor, Criteria("all"), "xlsx"));

        table.Skip(1).ShouldHaveSingleItem()[0].ShouldBe(Format(Today().AddDays(FirstDay), FileDateFormat));
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("xlsx")]
    [InlineData("docx")]
    public async Task Cada_exportacion_queda_en_el_registro_con_su_formato_filtros_y_cantidad_de_filas(string format)
    {
        var doctor = await SeedDoctorAsync();
        await SeedVariedAppointmentsAsync(doctor);
        var criteria = Criteria("pending", sortBy: "status");

        await ExportAsync(doctor, criteria, format);

        var log = (await ReadLogsAsync(doctor.DoctorId, """{"type":"pending","sort_by":"status"}""")).ShouldHaveSingleItem();
        log.Action.ShouldBe("export");
        log.ReportType.ShouldBe("pending");
        log.ExportFormat.ShouldBe(format);
        log.RowCount.ShouldBe(2);
        log.FiltersMatch.ShouldBeTrue();
    }

    [Fact]
    public async Task Un_reporte_sin_datos_no_se_exporta_responde_400_con_un_mensaje_y_no_se_registra()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAsync(doctor, patient, Pending, FirstDay);

        using var response = await doctor.Client.PostAsJsonAsync(ExportUrl, Body(Criteria("completed"), "pdf"));

        var title = await AssertErrorAsync(response, HttpStatusCode.BadRequest, EmptyReportCode);
        title.ShouldNotBeNullOrWhiteSpace();
        (await ReadLogsAsync(doctor.DoctorId, "{}")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"type":"all","from":"2027-01-01","to":"2027-01-31"}""")]
    [InlineData("""{"format":"csv","type":"all","from":"2027-01-01","to":"2027-01-31"}""")]
    [InlineData("""{"format":"pdf","from":"2027-01-01","to":"2027-01-31"}""")]
    public async Task Sin_formato_con_un_formato_desconocido_o_sin_tipo_responde_400(string json)
    {
        var doctor = await SeedDoctorAsync();

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await doctor.Client.PostAsync(ExportUrl, content);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await ReadLogsAsync(doctor.DoctorId, "{}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_paciente_recibe_403_y_sin_token_responde_401()
    {
        var patient = await SeedPatientAsync();
        using var anonymous = Factory.CreateClient();

        using var asPatient = await patient.Client.PostAsJsonAsync(ExportUrl, Body(Criteria("all"), "pdf"));
        using var withoutToken = await anonymous.PostAsJsonAsync(ExportUrl, Body(Criteria("all"), "pdf"));

        await asPatient.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await withoutToken.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Tres pacientes y cuatro estados, con y sin motivo de consulta, para que cada orden se distinga.
    private async Task SeedVariedAppointmentsAsync(DoctorSeed doctor)
    {
        var bruno = await SeedNamedPatientAsync("Bruno", "Díaz");
        var ana = await SeedNamedPatientAsync("Ana", "Pérez");
        var carla = await SeedNamedPatientAsync("Carla", "Gómez");
        await SeedAsync(doctor, carla, Confirmed, FirstDay, ReasonForVisit);
        await SeedAsync(doctor, ana, Completed, FirstDay + 1);
        await SeedAsync(doctor, bruno, Pending, FirstDay + 2, ReasonForVisit);
        await SeedAsync(doctor, ana, Cancelled, FirstDay + 3);
    }

    private async Task SeedAsync(
        DoctorSeed doctor, PatientSeed patient, string status, int day, string? reasonForVisit = null)
    {
        var startsAtUtc = Local(Today().AddDays(day), AppointmentStartHour).ToUniversalTime();
        await InsertBlockAsync(doctor.DoctorId, startsAtUtc, startsAtUtc.AddHours(AppointmentHours));
        await WithContextAsync(context => TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status,
            status == Rejected ? RejectionReason : null, reasonForVisit));
    }

    private async Task<PatientSeed> SeedNamedPatientAsync(string firstName, string lastName)
    {
        var patient = await SeedPatientAsync();
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.patient SET first_name = {firstName}, last_name = {lastName} WHERE id = {patient.PatientId}"));
        return patient;
    }

    private Dictionary<string, object> Criteria(string type, string? sortBy = null, string? sortDirection = null)
    {
        var criteria = new Dictionary<string, object>
        {
            ["type"] = type,
            ["from"] = Format(Today().AddDays(FirstDay), DateFormat),
            ["to"] = Format(Today().AddDays(LastDay), DateFormat)
        };
        if (sortBy is not null) criteria["sortBy"] = sortBy;
        if (sortDirection is not null) criteria["sortDirection"] = sortDirection;
        return criteria;
    }

    private static Dictionary<string, object> Body(Dictionary<string, object> criteria, string format) =>
        new(criteria) { ["format"] = format };

    // La pantalla se consulta con los mismos criterios y una sola página que contiene todas las filas.
    private static async Task<AppointmentReportResponse> GetScreenAsync(DoctorSeed doctor, Dictionary<string, object> criteria)
    {
        var query = string.Join('&', criteria.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value.ToString()!)}"));
        using var response = await doctor.Client.GetAsync($"{ReportsUrl}?{query}&pageSize={SortedQueryPageSize}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<AppointmentReportResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldNotBeNull();
    }

    private static async Task<byte[]> ExportAsync(DoctorSeed doctor, Dictionary<string, object> criteria, string format)
    {
        using var response = await doctor.Client.PostAsJsonAsync(ExportUrl, Body(criteria, format));
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static IReadOnlyList<string> ToFileRow(AppointmentReportRowResponse row) =>
    [
        Format(row.Date, FileDateFormat),
        row.StartTime.ToString(FileTimeFormat, CultureInfo.InvariantCulture),
        row.PatientName,
        row.SpecialtyName,
        StatusNames[row.Status],
        row.ReasonForVisit ?? string.Empty
    ];

    // Las filas del médico cuyos filtros contienen los esperados; "{}" las devuelve todas.
    private async Task<List<LogRow>> ReadLogsAsync(int doctorId, string expectedFilters)
    {
        var rows = new List<LogRow>();
        await WithContextAsync(async context => rows = await context.Database.SqlQuery<LogRow>(
            $"""
            SELECT action, report_type, export_format, row_count, filters @> {expectedFilters}::jsonb AS filters_match
            FROM scheduling.appointment_report_log
            WHERE doctor_id = {doctorId}
            """).ToListAsync());
        return rows;
    }

    private static string Format(DateOnly date, string format) => date.ToString(format, CultureInfo.InvariantCulture);
}
