using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración del historial y del detalle de citas del paciente contra la API real. Cada cita se siembra
/// con su propio bloque, que la cubre exactamente, para que los bloques de un mismo médico no se crucen.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PatientAppointmentHistoryTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string PatientAppointmentsUrl = "/api/patients/me/appointments";
    private const string DateFormat = "yyyy-MM-dd";
    private const string DoctorFullName = "Luis Pérez";
    private const string EmptyListMessage = "No tienes citas registradas.";
    private const string StatusReason = "No puedo asistir ese día";
    private const int PastDays = 5;
    private const int RecentPastDays = 2;
    private const int FutureDays = 3;
    private const int FirstDayOffset = 1;
    private const int DayCount = 5;
    private const int SecondPage = 2;
    private const int PageSize = 2;
    private const int OversizedPageSize = 101;
    private const int MissingAppointmentId = 999_999;
    private const int NotDefinedStatus = 5;
    private const int BeforeRangeHour = 21;
    private const int RangeStartHour = 0;
    private const int RangeEndHour = 22;
    private const int AfterRangeHour = 1;

    [Fact]
    public async Task Lista_mis_citas_pasadas_y_futuras_con_datos_locales_de_la_mas_reciente_a_la_mas_antigua()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var other = await SeedPatientAsync();
        var completedId = await SeedAppointmentAsync(doctor, patient, Completed, At(-PastDays));
        var cancelledId = await SeedAppointmentAsync(doctor, patient, Cancelled, At(-RecentPastDays));
        var futureId = await SeedAppointmentAsync(doctor, patient, Pending, At(FutureDays));
        await SeedAppointmentAsync(doctor, other, Pending, At(FutureDays + 1));

        var list = await ListAsync(patient);

        list.Items.Select(item => item.AppointmentId).ShouldBe(new[] { futureId, cancelledId, completedId });
        list.Items.Select(item => item.Status).ShouldBe(new[] { Pending, Cancelled, Completed });
        list.TotalCount.ShouldBe(3);
        list.Message.ShouldBeNull();
        var first = list.Items[0];
        first.AppointmentDate.ShouldBe(Today().AddDays(FutureDays));
        first.StartTime.ShouldBe(new TimeOnly(AppointmentStartHour, 0));
        first.EndTime.ShouldBe(new TimeOnly(AppointmentStartHour + AppointmentHours, 0));
        first.DoctorId.ShouldBe(doctor.DoctorId);
        first.DoctorName.ShouldBe(DoctorFullName);
        first.SpecialtyId.ShouldBe(doctor.SpecialtyId);
        first.SpecialtyName.ShouldBe(await SpecialtyNameAsync(doctor.SpecialtyId));
    }

    [Fact]
    public async Task Filtrar_por_estado_cancelado_devuelve_solo_las_canceladas()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var olderCancelled = await SeedAppointmentAsync(doctor, patient, Cancelled, At(-PastDays));
        var newerCancelled = await SeedAppointmentAsync(doctor, patient, Cancelled, At(-RecentPastDays));
        await SeedAppointmentAsync(doctor, patient, Completed, At(-FutureDays));
        await SeedAppointmentAsync(doctor, patient, Pending, At(FutureDays));

        var list = await ListAsync(patient, $"?status={Cancelled}");

        list.Items.Select(item => item.AppointmentId).ShouldBe(new[] { newerCancelled, olderCancelled });
        list.Items.ShouldAllBe(item => item.Status == Cancelled);
        list.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Filtrar_por_fechas_incluye_las_citas_de_los_dos_dias_extremos_y_excluye_las_de_fuera()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var from = Today().AddDays(FirstDayOffset + 1);
        var to = Today().AddDays(FirstDayOffset + 3);
        await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDayOffset, BeforeRangeHour));
        var fromDay = await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDayOffset + 1, RangeStartHour));
        var middle = await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDayOffset + 2));
        var toDay = await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDayOffset + 3, RangeEndHour));
        await SeedAppointmentAsync(doctor, patient, Pending, At(FirstDayOffset + 4, AfterRangeHour));

        var list = await ListAsync(patient, $"?from={Format(from)}&to={Format(to)}");

        list.Items.Select(item => item.AppointmentId).ShouldBe(new[] { toDay, middle, fromDay });
        list.TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task La_paginacion_devuelve_la_tercera_y_la_cuarta_cita_con_sus_metadatos()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var ids = new List<int>();
        for (var day = FirstDayOffset; day < FirstDayOffset + DayCount; day++)
        {
            ids.Add(await SeedAppointmentAsync(doctor, patient, Pending, At(day)));
        }

        var list = await ListAsync(patient, $"?pageNumber={SecondPage}&pageSize={PageSize}");

        // Descendente por inicio: la 3.ª y la 4.ª más recientes son las sembradas en los días 3 y 2.
        list.Items.Select(item => item.AppointmentId).ShouldBe(new[] { ids[2], ids[1] });
        list.PageNumber.ShouldBe(SecondPage);
        list.PageSize.ShouldBe(PageSize);
        list.TotalCount.ShouldBe(DayCount);
    }

    [Fact]
    public async Task Un_paciente_sin_citas_recibe_la_lista_vacia_con_el_mensaje()
    {
        var patient = await SeedPatientAsync();

        var list = await ListAsync(patient);

        list.Items.ShouldBeEmpty();
        list.TotalCount.ShouldBe(0);
        list.Message.ShouldBe(EmptyListMessage);
    }

    [Fact]
    public async Task Un_rango_con_la_fecha_inicial_posterior_a_la_final_se_rechaza()
    {
        var patient = await SeedPatientAsync();
        var from = Format(Today().AddDays(FirstDayOffset + 1));
        var to = Format(Today().AddDays(FirstDayOffset));

        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}?from={from}&to={to}");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.InvalidDateRange");
    }

    [Fact]
    public async Task Un_tamano_de_pagina_mayor_al_maximo_se_rechaza()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}?pageSize={OversizedPageSize}");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.InvalidPageSize");
    }

    [Fact]
    public async Task Un_estado_numerico_no_definido_se_rechaza_antes_de_llegar_al_handler()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}?status={NotDefinedStatus}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task El_detalle_de_una_cita_propia_pendiente_trae_los_datos_de_la_lista_sin_motivos()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var appointmentId = await SeedAppointmentAsync(doctor, patient, Pending, At(FutureDays));

        var detail = await GetDetailAsync(patient, appointmentId);
        var listed = (await ListAsync(patient)).Items.ShouldHaveSingleItem();

        detail.Appointment.ShouldBe(listed);
        detail.ReasonForVisit.ShouldBeNull();
        detail.StatusReason.ShouldBeNull();
    }

    [Theory]
    [InlineData(Cancelled)]
    [InlineData(Rejected)]
    public async Task El_detalle_de_una_cita_cancelada_o_rechazada_trae_el_motivo_de_estado(string status)
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var appointmentId = await SeedAppointmentAsync(doctor, patient, status, At(FutureDays), StatusReason);

        var detail = await GetDetailAsync(patient, appointmentId);

        detail.Appointment.Status.ShouldBe(status);
        detail.StatusReason.ShouldBe(StatusReason);
    }

    [Fact]
    public async Task El_detalle_de_una_cita_de_otro_paciente_responde_que_no_existe()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var other = await SeedPatientAsync();
        var othersAppointmentId = await SeedAppointmentAsync(doctor, other, Pending, At(FutureDays));

        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}/{othersAppointmentId}");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task El_detalle_de_una_cita_inexistente_responde_que_no_existe()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}/{MissingAppointmentId}");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task Un_medico_autenticado_no_puede_usar_la_lista_ni_el_detalle()
    {
        var doctor = await SeedDoctorAsync();

        using var list = await doctor.Client.GetAsync(PatientAppointmentsUrl);
        using var detail = await doctor.Client.GetAsync($"{PatientAppointmentsUrl}/{MissingAppointmentId}");

        await list.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await detail.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_token_la_lista_y_el_detalle_responden_401()
    {
        using var anonymous = Factory.CreateClient();

        using var list = await anonymous.GetAsync(PatientAppointmentsUrl);
        using var detail = await anonymous.GetAsync($"{PatientAppointmentsUrl}/{MissingAppointmentId}");

        await list.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await detail.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    private async Task<ListPatientAppointmentsResponse> ListAsync(PatientSeed patient, string query = "")
    {
        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}{query}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<ListPatientAppointmentsResponse>()).ShouldNotBeNull();
    }

    private async Task<PatientAppointmentDetailResponse> GetDetailAsync(PatientSeed patient, int appointmentId)
    {
        using var response = await patient.Client.GetAsync($"{PatientAppointmentsUrl}/{appointmentId}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<PatientAppointmentDetailResponse>()).ShouldNotBeNull();
    }

    // El bloque cubre exactamente la cita; los instantes llegan en UTC porque Npgsql solo escribe timestamptz en UTC.
    private async Task<int> SeedAppointmentAsync(
        DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc, string? statusReason = null)
    {
        await InsertBlockAsync(doctor.DoctorId, startsAtUtc, startsAtUtc.AddHours(AppointmentHours));
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status, statusReason));

        return appointmentId;
    }

    private async Task<string> SpecialtyNameAsync(int specialtyId)
    {
        var name = string.Empty;
        await WithContextAsync(async context =>
            name = await context.Specialties.Where(specialty => specialty.Id == specialtyId)
                .Select(specialty => specialty.Name).SingleAsync());

        return name;
    }

    // Inicio local en punto, a los días indicados desde hoy, expresado en UTC.
    private DateTimeOffset At(int dayOffset, int hour = AppointmentStartHour) =>
        Local(Today().AddDays(dayOffset), hour).ToUniversalTime();

    private static string Format(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
