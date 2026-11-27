using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class DoctorScheduleTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string Url = "/api/doctors/me/appointments";

    [Fact]
    public async Task El_medico_ve_solo_sus_citas_activas_con_paciente_especialidad_fecha_hora_y_estado()
    {
        var doctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var day = Today().AddDays(DaysAhead);
        var own = await InsertAppointmentAsync(patient, doctor, Local(day, 9), Confirmed);
        await InsertAppointmentAsync(patient, doctor, Local(day.AddDays(1), 9), Cancelled);
        // El mismo paciente no puede tener dos citas activas a la vez, aunque sean con médicos distintos.
        await InsertAppointmentAsync(patient, other, Local(day, 13), Pending);

        using var response = await doctor.Client.GetAsync(Url);

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<DoctorScheduleResponse>())!;
        page.TotalCount.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().ShouldBe(new DoctorScheduleItemResponse(
            own, patient.PatientId, "Ana Pérez", doctor.SpecialtyId, await SpecialtyNameAsync(doctor.SpecialtyId),
            Local(day, 9), Local(day, 11), day, new TimeOnly(9, 0), new TimeOnly(11, 0), "confirmed", false));
    }

    [Fact]
    public async Task Combina_estado_rango_y_paciente_y_ordena_por_fecha_descendente()
    {
        var doctor = await SeedDoctorAsync();
        var maria = await SeedPatientAsync("María", "Gómez");
        var luis = await SeedPatientAsync("Luis", "Peña");
        var day = Today().AddDays(DaysAhead);
        var first = await InsertAppointmentAsync(maria, doctor, Local(day, 8), Confirmed);
        var second = await InsertAppointmentAsync(maria, doctor, Local(day.AddDays(2), 8), Confirmed);
        await InsertAppointmentAsync(maria, doctor, Local(day.AddDays(1), 8), Pending);
        await InsertAppointmentAsync(maria, doctor, Local(day.AddDays(10), 8), Confirmed);
        await InsertAppointmentAsync(luis, doctor, Local(day, 12), Confirmed);

        using var response = await doctor.Client.GetAsync(
            $"{Url}?status=confirmed&from={day:yyyy-MM-dd}&to={day.AddDays(6):yyyy-MM-dd}&patientName=MARIA&sort=descending");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<DoctorScheduleResponse>())!;
        page.Items.Select(item => item.AppointmentId).ShouldBe(new[] { second, first });
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task La_lista_es_paginada()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var day = Today().AddDays(DaysAhead);
        var ids = new List<int>();
        for (var offset = 0; offset < 5; offset++)
            ids.Add(await InsertAppointmentAsync(patient, doctor, Local(day.AddDays(offset), 8), Pending));

        using var response = await doctor.Client.GetAsync($"{Url}?pageNumber=2&pageSize=2");

        var page = (await response.Content.ReadFromJsonAsync<DoctorScheduleResponse>())!;
        page.Items.Select(item => item.AppointmentId).ShouldBe(ids.Skip(2).Take(2));
        (page.PageNumber, page.PageSize, page.TotalCount).ShouldBe((2, 2, 5));
    }

    [Fact]
    public async Task La_agenda_no_filtra_por_estados_que_no_son_activos()
    {
        var doctor = await SeedDoctorAsync();

        using var response = await doctor.Client.GetAsync($"{Url}?status=cancelled");

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.InvalidScheduleStatus");
    }

    [Fact]
    public async Task El_calendario_devuelve_todas_las_citas_de_hasta_31_dias_sin_paginar()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var from = Today().AddDays(DaysAhead);
        var ids = new List<int>();
        for (var offset = 0; offset < 25; offset++)
            ids.Add(await InsertAppointmentAsync(patient, doctor, Local(from.AddDays(offset), 8), Pending));
        await InsertAppointmentAsync(patient, doctor, Local(from.AddDays(31), 8), Pending);

        using var month = await doctor.Client.GetAsync($"{Url}/calendar?from={from:yyyy-MM-dd}&to={from.AddDays(30):yyyy-MM-dd}");
        using var tooLong = await doctor.Client.GetAsync($"{Url}/calendar?from={from:yyyy-MM-dd}&to={from.AddDays(31):yyyy-MM-dd}");

        await month.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await month.Content.ReadFromJsonAsync<DoctorCalendarResponse>())!.Items.Select(item => item.AppointmentId).ShouldBe(ids);
        await AssertErrorAsync(tooLong, HttpStatusCode.BadRequest, "Appointment.DateRangeTooLong");
    }

    [Fact]
    public async Task El_detalle_muestra_el_motivo_y_el_contacto_del_paciente()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var day = Today().AddDays(DaysAhead);
        var id = await InsertAppointmentAsync(patient, doctor, Local(day, 9), Pending, "Dolor de pecho");

        using var response = await doctor.Client.GetAsync($"{Url}/{id}");

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var detail = (await response.Content.ReadFromJsonAsync<DoctorAppointmentDetailResponse>())!;
        detail.ReasonForVisit.ShouldBe("Dolor de pecho");
        detail.PatientName.ShouldBe("Ana Pérez");
        detail.PatientPhoneNumber.ShouldBe("8095550100");
        detail.PatientEmail.ShouldBe(patient.Email);
        detail.Status.ShouldBe("pending");
        detail.StartsAt.ShouldBe(Local(day, 9));
    }

    [Fact]
    public async Task La_cita_de_otro_medico_responde_404_y_un_paciente_no_ve_la_agenda()
    {
        var doctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var id = await InsertAppointmentAsync(patient, other, Local(Today().AddDays(DaysAhead), 9), Pending);

        using var detail = await doctor.Client.GetAsync($"{Url}/{id}");
        using var list = await patient.Client.GetAsync(Url);
        using var patientDetail = await patient.Client.GetAsync($"{Url}/{id}");

        await AssertErrorAsync(detail, HttpStatusCode.NotFound, "Appointment.NotFound");
        await list.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await patientDetail.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    private async Task<PatientSeed> SeedPatientAsync(string firstName, string lastName)
    {
        var patient = await SeedPatientAsync();
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.patient SET first_name = {firstName}, last_name = {lastName} WHERE id = {patient.PatientId}"));
        return patient;
    }

    // Cada cita tiene su propio bloque, que la cubre exactamente.
    private async Task<int> InsertAppointmentAsync(
        PatientSeed patient, DoctorSeed doctor, DateTimeOffset startsAt, string status, string? reasonForVisit = null)
    {
        var startsAtUtc = startsAt.ToUniversalTime();
        await InsertBlockAsync(doctor.DoctorId, startsAtUtc, startsAtUtc.AddHours(AppointmentHours));
        var id = 0;
        await WithContextAsync(async context =>
        {
            id = await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status,
                status == Cancelled ? "Cancelada antes" : null);
            if (reasonForVisit is not null)
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE scheduling.appointment SET reason_for_visit = {reasonForVisit} WHERE id = {id}");
        });
        return id;
    }

    private async Task<string> SpecialtyNameAsync(int specialtyId)
    {
        var name = string.Empty;
        await WithContextAsync(async context => name = await context.Specialties
            .Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync());
        return name;
    }
}
