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
/// Pruebas de integración de la generación de reportes de citas contra la API real: las citas del médico
/// autenticado, los totales y las filas en hora local, la ausencia de identificadores, la fila del registro de
/// reportes y el reporte vacío. Cada cita se siembra con su propio bloque, que la cubre exactamente.
/// </summary>
[Trait("Category", "Integration")]
public sealed class GenerateAppointmentReportTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string ReportsUrl = "/api/doctors/me/appointment-reports";
    private const string DateFormat = "yyyy-MM-dd";
    private const string PatientFullName = "Ana Pérez";
    private const string ReasonForVisit = "Control anual";
    private const string EmptyReportMessage = "No hay citas del tipo y el rango de fechas elegidos.";
    private const string IdSuffix = "Id";
    private const int FirstDay = 1;
    private const int LastDay = 10;
    private const int OutOfRangeDay = 12;
    private const int OversizedPageSize = 101;
    private const int PatientNameMaxLength = 201;

    private static readonly string[] StatusesInLifecycleOrder = [Pending, Confirmed, Rejected, Cancelled, Completed];

    // Los filtros opcionales sin valor se omiten: PageSize y el orden no admiten null en la petición.
    private static readonly JsonSerializerOptions IgnoreNulls = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private sealed record LogRow(
        string Action, string ReportType, string? ExportFormat, int RowCount, DateTimeOffset GeneratedAt, bool FiltersMatch);

    [Fact]
    public async Task Genera_los_totales_por_estado_y_las_filas_de_mis_citas_del_rango_en_hora_local()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAllStatusesAsync(doctor, patient);
        await SeedAppointmentAsync(doctor, patient, Pending, At(OutOfRangeDay));

        var report = await GenerateAsync(doctor, Body("all"));

        report.Totals.Select(total => total.Status).ShouldBe(StatusesInLifecycleOrder);
        report.Totals.ShouldAllBe(total => total.Count == 1);
        report.TotalCount.ShouldBe(StatusesInLifecycleOrder.Length);
        report.Message.ShouldBeNull();
        var first = report.Items[0];
        first.ShouldBe(new AppointmentReportRowResponse(
            Today().AddDays(FirstDay), new TimeOnly(AppointmentStartHour, 0), PatientFullName,
            await SpecialtyNameAsync(doctor.SpecialtyId), Pending, ReasonForVisit));
        report.Items.Select(item => item.Date).ShouldBe(report.Items.Select(item => item.Date).Order());
    }

    [Fact]
    public async Task Solo_aparecen_las_citas_del_medico_autenticado()
    {
        var doctor = await SeedDoctorAsync();
        var otherDoctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDay));
        await SeedAppointmentAsync(otherDoctor, patient, Pending, At(FirstDay + 1));
        await SeedAppointmentAsync(otherDoctor, patient, Confirmed, At(FirstDay + 2));

        var mine = await GenerateAsync(doctor, Body("all"));
        var theirs = await GenerateAsync(otherDoctor, Body("all"));

        mine.TotalCount.ShouldBe(1);
        mine.Items.ShouldHaveSingleItem().Date.ShouldBe(Today().AddDays(FirstDay));
        theirs.TotalCount.ShouldBe(2);
    }

    [Theory]
    [InlineData("completed", new[] { Completed })]
    [InlineData("cancelled", new[] { Rejected, Cancelled })]
    [InlineData("pending", new[] { Pending, Confirmed })]
    public async Task Cada_tipo_incluye_sus_estados(string type, string[] expectedStatuses)
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAllStatusesAsync(doctor, patient);

        var report = await GenerateAsync(doctor, Body(type));

        report.Items.Select(item => item.Status).ShouldBe(expectedStatuses, ignoreOrder: true);
        report.Totals.Where(total => total.Count > 0).Select(total => total.Status)
            .ShouldBe(expectedStatuses, ignoreOrder: true);
    }

    [Fact]
    public async Task La_respuesta_no_contiene_identificadores_internos()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAllStatusesAsync(doctor, patient);

        using var response = await doctor.Client.PostAsJsonAsync(ReportsUrl, Body("all"), IgnoreNulls);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        PropertyNames(json.RootElement).ShouldAllBe(
            name => !name.Equals(IdSuffix, StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(IdSuffix, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cada_generacion_queda_en_el_registro_con_su_tipo_filtros_y_el_total_de_filas()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDay));
        await SeedAppointmentAsync(doctor, patient, Confirmed, At(FirstDay + 1));
        await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDay + 2));
        var body = Body("pending", patientName: "ana", sortBy: "patient", pageSize: 2);

        var report = await GenerateAsync(doctor, body);

        report.Items.Count.ShouldBe(2);
        report.TotalCount.ShouldBe(3);
        var expectedFilters =
            $$"""{"type":"pending","from":"{{Format(Today().AddDays(FirstDay))}}","to":"{{Format(Today().AddDays(LastDay))}}","patient_name":"ana","sort_by":"patient"}""";
        var log = (await ReadLogsAsync(doctor.DoctorId, expectedFilters)).ShouldHaveSingleItem();
        log.Action.ShouldBe("generate");
        log.ReportType.ShouldBe("pending");
        log.ExportFormat.ShouldBeNull();
        log.RowCount.ShouldBe(3);
        log.GeneratedAt.ShouldBe(Clock.UtcNow, TimeSpan.FromMilliseconds(1));
        log.FiltersMatch.ShouldBeTrue();
    }

    [Fact]
    public async Task Sin_citas_del_tipo_y_rango_responde_200_con_el_mensaje_y_los_totales_en_cero_y_se_registra()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDay));

        var report = await GenerateAsync(doctor, Body("completed"));

        report.Items.ShouldBeEmpty();
        report.TotalCount.ShouldBe(0);
        report.Totals.Select(total => total.Status).ShouldBe(StatusesInLifecycleOrder);
        report.Totals.ShouldAllBe(total => total.Count == 0);
        report.Message.ShouldBe(EmptyReportMessage);
        (await ReadLogsAsync(doctor.DoctorId, "{}")).ShouldHaveSingleItem().RowCount.ShouldBe(0);
    }

    public static IEnumerable<object[]> InvalidBodies()
    {
        yield return [new { type = "all" }, "Report.DateRangeRequired"];
        yield return [new { type = "all", from = "2027-01-31", to = "2027-01-01" }, "Report.InvalidDateRange"];
        yield return [new { type = "all", from = "2027-01-01", to = "2027-01-31", pageSize = OversizedPageSize }, "Report.InvalidPageSize"];
        yield return [new { type = "all", from = "2027-01-01", to = "2027-01-31", patientName = new string('a', PatientNameMaxLength + 1) }, "Report.PatientNameTooLong"];
        yield return [new { type = "all", from = "2027-01-01", to = "2027-01-31", specialtyId = 0 }, "Report.InvalidSpecialtyId"];
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Unos_criterios_invalidos_responden_400_con_su_codigo_y_no_se_registran(object body, string code)
    {
        var doctor = await SeedDoctorAsync();

        using var response = await doctor.Client.PostAsJsonAsync(ReportsUrl, body);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
        (await ReadLogsAsync(doctor.DoctorId, "{}")).ShouldBeEmpty();
    }

    [Theory]
    [InlineData("""{"from":"2027-01-01","to":"2027-01-31"}""")]
    [InlineData("""{"type":"desconocido","from":"2027-01-01","to":"2027-01-31"}""")]
    public async Task Una_peticion_sin_tipo_o_con_un_nombre_desconocido_responde_400(string json)
    {
        var doctor = await SeedDoctorAsync();

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await doctor.Client.PostAsync(ReportsUrl, content);

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await ReadLogsAsync(doctor.DoctorId, "{}")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_paciente_recibe_403_y_sin_token_responde_401()
    {
        var patient = await SeedPatientAsync();
        using var anonymous = Factory.CreateClient();

        using var asPatient = await patient.Client.PostAsJsonAsync(ReportsUrl, Body("all"), IgnoreNulls);
        using var withoutToken = await anonymous.PostAsJsonAsync(ReportsUrl, Body("all"), IgnoreNulls);

        await asPatient.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await withoutToken.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Una cita por estado, cada una en su propio día del rango, en el orden del ciclo de vida.
    private async Task SeedAllStatusesAsync(DoctorSeed doctor, PatientSeed patient)
    {
        for (var index = 0; index < StatusesInLifecycleOrder.Length; index++)
        {
            var status = StatusesInLifecycleOrder[index];
            await SeedAppointmentAsync(
                doctor, patient, status, At(FirstDay + index), status == Rejected ? RejectionReason : null);
        }
    }

    private async Task SeedAppointmentAsync(
        DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc, string? statusReason = null)
    {
        await InsertBlockAsync(doctor.DoctorId, startsAtUtc, startsAtUtc.AddHours(AppointmentHours));
        await WithContextAsync(context => TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status,
            statusReason, ReasonForVisit));
    }

    private object Body(
        string type, string? patientName = null, string? sortBy = null, int? pageSize = null) =>
        new
        {
            type,
            from = Format(Today().AddDays(FirstDay)),
            to = Format(Today().AddDays(LastDay)),
            patientName,
            sortBy,
            pageSize
        };

    private static async Task<AppointmentReportResponse> GenerateAsync(DoctorSeed doctor, object body)
    {
        using var response = await doctor.Client.PostAsJsonAsync(ReportsUrl, body, IgnoreNulls);
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<AppointmentReportResponse>()).ShouldNotBeNull();
    }

    // Las filas del médico cuyos filtros contienen los esperados; "{}" las devuelve todas.
    private async Task<List<LogRow>> ReadLogsAsync(int doctorId, string expectedFilters)
    {
        var rows = new List<LogRow>();
        await WithContextAsync(async context => rows = await context.Database.SqlQuery<LogRow>(
            $"""
            SELECT action, report_type, export_format, row_count, generated_at,
                   filters @> {expectedFilters}::jsonb AS filters_match
            FROM scheduling.appointment_report_log
            WHERE doctor_id = {doctorId}
            """).ToListAsync());
        return rows;
    }

    private async Task<string> SpecialtyNameAsync(int specialtyId)
    {
        var name = string.Empty;
        await WithContextAsync(async context => name = await context.Specialties
            .Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync());
        return name;
    }

    private static IEnumerable<string> PropertyNames(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject()
            .SelectMany(property => PropertyNames(property.Value).Prepend(property.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(PropertyNames),
        _ => []
    };

    private DateTimeOffset At(int day) => Local(Today().AddDays(day), AppointmentStartHour).ToUniversalTime();

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
