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
/// Pruebas de integración del reporte filtrado contra la API real: los filtros solos y combinados, cada orden en los
/// dos sentidos, la paginación, el resultado vacío y que filtrar no deja filas en el registro de reportes. Cada cita
/// se siembra en su propio día, con un bloque que la cubre exactamente.
/// </summary>
[Trait("Category", "Integration")]
public sealed class FilterAppointmentReportTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string ReportsUrl = "/api/doctors/me/appointment-reports";
    private const string DateFormat = "yyyy-MM-dd";
    private const string EmptyReportMessage = "No hay citas del tipo y el rango de fechas elegidos.";
    private const int FirstDay = 1;
    private const int LastDay = 20;
    private const int PageSize = 2;
    private const int SecondPage = 2;

    private static readonly string[] StatusesInLifecycleOrder = [Pending, Confirmed, Rejected, Cancelled, Completed];

    private sealed record Seeded(DateOnly Date, string PatientName, string Status, int SpecialtyId);

    [Fact]
    public async Task Los_filtros_combinados_por_estado_fechas_paciente_y_especialidad_devuelven_solo_lo_que_cumple_todos()
    {
        var doctor = await SeedDoctorAsync();
        var otherSpecialty = await AddSpecialtyAsync(doctor);
        var ana = await SeedNamedPatientAsync("Ana", "Pérez");
        var jose = await SeedNamedPatientAsync("José", "Martínez");
        await SeedAsync(doctor, jose, Cancelled, FirstDay + 1, otherSpecialty);
        await SeedAsync(doctor, jose, Cancelled, FirstDay + 2);
        await SeedAsync(doctor, jose, Pending, FirstDay + 3, otherSpecialty);
        await SeedAsync(doctor, ana, Cancelled, FirstDay + 4, otherSpecialty);
        await SeedAsync(doctor, jose, Cancelled, LastDay + 2, otherSpecialty);

        var report = await GetAsync(doctor,
            $"type=all&{Range()}&status=cancelled&patientName=JOSE%20mart&specialtyId={otherSpecialty}");

        var row = report.Items.ShouldHaveSingleItem();
        row.Date.ShouldBe(Today().AddDays(FirstDay + 1));
        row.PatientName.ShouldBe("José Martínez");
        row.Status.ShouldBe(Cancelled);
        report.TotalCount.ShouldBe(1);
        report.Totals.Single(total => total.Status == Cancelled).Count.ShouldBe(1);
    }

    [Fact]
    public async Task El_filtro_de_fechas_incluye_los_dos_dias_extremos()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        foreach (var day in new[] { FirstDay, FirstDay + 1, FirstDay + 2, FirstDay + 3, FirstDay + 4 })
            await SeedAsync(doctor, patient, Pending, day);

        var report = await GetAsync(doctor,
            $"type=all&from={Format(Today().AddDays(FirstDay + 1))}&to={Format(Today().AddDays(FirstDay + 3))}");

        report.Items.Select(item => item.Date).ShouldBe(new[]
        {
            Today().AddDays(FirstDay + 1), Today().AddDays(FirstDay + 2), Today().AddDays(FirstDay + 3)
        });
    }

    [Theory]
    [InlineData("date", "ascending")]
    [InlineData("date", "descending")]
    [InlineData("patient", "ascending")]
    [InlineData("patient", "descending")]
    [InlineData("status", "ascending")]
    [InlineData("status", "descending")]
    public async Task Ordena_por_cada_columna_en_los_dos_sentidos(string sortBy, string sortDirection)
    {
        var doctor = await SeedDoctorAsync();
        var seeded = await SeedForSortingAsync(doctor);

        var report = await GetAsync(doctor, $"type=all&{Range()}&sortBy={sortBy}&sortDirection={sortDirection}");

        var expected = Sorted(seeded, sortBy, sortDirection == "descending");
        report.Items.Select(item => (item.Date, item.PatientName, item.Status))
            .ShouldBe(expected.Select(item => (item.Date, item.PatientName, item.Status)));
    }

    [Fact]
    public async Task Devuelve_la_pagina_pedida_con_los_totales_de_todo_el_resultado()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        for (var day = FirstDay; day < FirstDay + 5; day++)
            await SeedAsync(doctor, patient, Pending, day);

        var report = await GetAsync(doctor, $"type=all&{Range()}&pageNumber={SecondPage}&pageSize={PageSize}");

        report.PageNumber.ShouldBe(SecondPage);
        report.PageSize.ShouldBe(PageSize);
        report.TotalCount.ShouldBe(5);
        report.Items.Select(item => item.Date).ShouldBe(new[] { Today().AddDays(FirstDay + 2), Today().AddDays(FirstDay + 3) });
        report.Totals.Single(total => total.Status == Pending).Count.ShouldBe(5);
    }

    [Fact]
    public async Task Si_ningun_resultado_coincide_responde_200_con_el_mensaje_y_los_totales_en_cero()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAsync(doctor, patient, Pending, FirstDay);

        var report = await GetAsync(doctor, $"type=all&{Range()}&patientName=nadie");

        report.Items.ShouldBeEmpty();
        report.TotalCount.ShouldBe(0);
        report.Totals.Select(total => total.Status).ShouldBe(StatusesInLifecycleOrder);
        report.Totals.ShouldAllBe(total => total.Count == 0);
        report.Message.ShouldBe(EmptyReportMessage);
    }

    [Fact]
    public async Task Filtrar_u_ordenar_no_agrega_filas_al_registro_de_reportes()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAsync(doctor, patient, Pending, FirstDay);
        using (var generated = await doctor.Client.PostAsJsonAsync(ReportsUrl, new
        {
            type = "all",
            from = Format(Today().AddDays(FirstDay)),
            to = Format(Today().AddDays(LastDay))
        }))
        {
            await generated.ShouldHaveStatusAsync(HttpStatusCode.OK);
        }

        await GetAsync(doctor, $"type=all&{Range()}&status=pending");
        await GetAsync(doctor, $"type=pending&{Range()}&sortBy=patient&sortDirection=descending");
        await GetAsync(doctor, $"type=all&{Range()}&pageNumber=2&pageSize=1");

        (await CountLogsAsync(doctor.DoctorId)).ShouldBe(1);
    }

    [Fact]
    public async Task Solo_aparecen_las_citas_del_medico_autenticado()
    {
        var doctor = await SeedDoctorAsync();
        var otherDoctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        await SeedAsync(doctor, patient, Pending, FirstDay);
        await SeedAsync(otherDoctor, patient, Pending, FirstDay + 1);

        var report = await GetAsync(doctor, $"type=all&{Range()}");

        report.Items.ShouldHaveSingleItem().Date.ShouldBe(Today().AddDays(FirstDay));
    }

    [Theory]
    [InlineData("pageNumber=0", "Report.InvalidPageNumber")]
    [InlineData("pageSize=101", "Report.InvalidPageSize")]
    [InlineData("specialtyId=0", "Report.InvalidSpecialtyId")]
    public async Task Unos_parametros_invalidos_responden_400_con_su_codigo(string parameter, string code)
    {
        var doctor = await SeedDoctorAsync();

        using var response = await doctor.Client.GetAsync($"{ReportsUrl}?type=all&{Range()}&{parameter}");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, code);
    }

    [Fact]
    public async Task Las_fechas_invertidas_o_ausentes_responden_400_con_su_codigo()
    {
        var doctor = await SeedDoctorAsync();

        using var inverted = await doctor.Client.GetAsync(
            $"{ReportsUrl}?type=all&from={Format(Today().AddDays(LastDay))}&to={Format(Today().AddDays(FirstDay))}");
        using var missing = await doctor.Client.GetAsync($"{ReportsUrl}?type=all");

        await AssertErrorAsync(inverted, HttpStatusCode.BadRequest, "Report.InvalidDateRange");
        await AssertErrorAsync(missing, HttpStatusCode.BadRequest, "Report.DateRangeRequired");
    }

    [Theory]
    [InlineData("")]
    [InlineData("type=desconocido&")]
    public async Task Sin_tipo_o_con_un_nombre_desconocido_responde_400(string type)
    {
        var doctor = await SeedDoctorAsync();

        using var response = await doctor.Client.GetAsync($"{ReportsUrl}?{type}{Range()}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Un_paciente_recibe_403_y_sin_token_responde_401()
    {
        var patient = await SeedPatientAsync();
        using var anonymous = Factory.CreateClient();

        using var asPatient = await patient.Client.GetAsync($"{ReportsUrl}?type=all&{Range()}");
        using var withoutToken = await anonymous.GetAsync($"{ReportsUrl}?type=all&{Range()}");

        await asPatient.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await withoutToken.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Tres pacientes y los cinco estados, en días distintos y sin que el orden de un criterio coincida con el de otro.
    private async Task<List<Seeded>> SeedForSortingAsync(DoctorSeed doctor)
    {
        var bruno = await SeedNamedPatientAsync("Bruno", "Díaz");
        var ana = await SeedNamedPatientAsync("Ana", "Pérez");
        var carla = await SeedNamedPatientAsync("Carla", "Gómez");
        return
        [
            await SeedAsync(doctor, carla, Confirmed, FirstDay),
            await SeedAsync(doctor, ana, Completed, FirstDay + 1),
            await SeedAsync(doctor, bruno, Pending, FirstDay + 2),
            await SeedAsync(doctor, ana, Rejected, FirstDay + 3),
            await SeedAsync(doctor, bruno, Cancelled, FirstDay + 4)
        ];
    }

    // A igual valor de la columna, el reporte desempata por fecha en el mismo sentido.
    private static List<Seeded> Sorted(List<Seeded> seeded, string sortBy, bool descending)
    {
        Func<Seeded, string> key = sortBy switch
        {
            "patient" => item => item.PatientName,
            "status" => item => Array.IndexOf(StatusesInLifecycleOrder, item.Status).ToString("D2", CultureInfo.InvariantCulture),
            _ => item => item.Date.ToString(DateFormat, CultureInfo.InvariantCulture)
        };

        return descending
            ? seeded.OrderByDescending(key, StringComparer.Ordinal).ThenByDescending(item => item.Date).ToList()
            : seeded.OrderBy(key, StringComparer.Ordinal).ThenBy(item => item.Date).ToList();
    }

    private async Task<Seeded> SeedAsync(
        DoctorSeed doctor, PatientSeed patient, string status, int day, int? specialtyId = null)
    {
        var startsAtUtc = Local(Today().AddDays(day), AppointmentStartHour).ToUniversalTime();
        var specialty = specialtyId ?? doctor.SpecialtyId;
        await InsertBlockAsync(doctor.DoctorId, startsAtUtc, startsAtUtc.AddHours(AppointmentHours));
        var patientName = string.Empty;
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patient.PatientId, doctor.DoctorId, specialty, startsAtUtc, AppointmentHours, status,
                status == Rejected ? RejectionReason : null);
            patientName = await context.Patients.Where(item => item.Id == patient.PatientId)
                .Select(item => item.FirstName + " " + item.LastName).SingleAsync();
        });

        return new Seeded(Today().AddDays(day), patientName, status, specialty);
    }

    private async Task<PatientSeed> SeedNamedPatientAsync(string firstName, string lastName)
    {
        var patient = await SeedPatientAsync();
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.patient SET first_name = {firstName}, last_name = {lastName} WHERE id = {patient.PatientId}"));
        return patient;
    }

    private async Task<int> AddSpecialtyAsync(DoctorSeed doctor)
    {
        var specialtyId = 0;
        await WithContextAsync(async context =>
        {
            specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctor.DoctorId, specialtyId);
        });
        return specialtyId;
    }

    private static async Task<AppointmentReportResponse> GetAsync(DoctorSeed doctor, string query)
    {
        using var response = await doctor.Client.GetAsync($"{ReportsUrl}?{query}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<AppointmentReportResponse>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web))).ShouldNotBeNull();
    }

    private async Task<int> CountLogsAsync(int doctorId)
    {
        var count = 0;
        await WithContextAsync(async context => count = (await context.Database.SqlQuery<int>(
            $"SELECT count(*)::int AS \"Value\" FROM scheduling.appointment_report_log WHERE doctor_id = {doctorId}")
            .ToListAsync()).Single());
        return count;
    }

    private string Range() =>
        $"from={Format(Today().AddDays(FirstDay))}&to={Format(Today().AddDays(LastDay))}";

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
