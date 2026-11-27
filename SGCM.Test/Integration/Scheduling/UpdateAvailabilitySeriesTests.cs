using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class UpdateAvailabilitySeriesTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string SeriesUrl = "/api/doctors/me/availability-series";
    private const string BlocksUrl = "/api/doctors/me/availability-blocks";
    private const string Reason = "Cambio de consultorio";

    // La serie original es lunes, martes y jueves de 8:00 a 14:00; la nueva, lunes, martes y miércoles de 10:00 a 16:00.
    private static readonly int[] MondayTuesdayThursday = [1, 2, 4];
    private static readonly int[] MondayTuesdayWednesday = [1, 2, 3];

    private sealed record BlockRow(int Id, int? SeriesId, DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsAvailable, bool IsModified);

    private DateOnly _monday;

    [Fact]
    public async Task Un_martes_la_serie_cambia_desde_el_lunes_siguiente_y_la_semana_actual_no_cambia()
    {
        var doctor = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        var seriesId = await CreateSeriesAsync(doctor);

        using var response = await doctor.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule());

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<UpdateAvailabilitySeriesResponse>())
            .ShouldBe(new UpdateAvailabilitySeriesResponse(Day(7), 0));
        var blocks = await ReadBlocksAsync(doctor.DoctorId);
        AssertBlock(blocks, Day(3), 8, 14);
        AssertBlock(blocks, Day(7), 10, 16);
        AssertBlock(blocks, Day(8), 10, 16);
        AssertBlock(blocks, Day(9), 10, 16);
        blocks.ShouldNotContain(block => LocalDate(block.StartsAt) == Day(10));
        blocks.Where(block => LocalDate(block.StartsAt) >= Day(7))
            .ShouldAllBe(block => block.SeriesId == seriesId && LocalDate(block.StartsAt).DayOfWeek <= DayOfWeek.Wednesday);
    }

    [Fact]
    public async Task Un_martes_editado_a_mano_conserva_su_horario()
    {
        var doctor = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        var seriesId = await CreateSeriesAsync(doctor);
        var tuesday = BlockOn(await ReadBlocksAsync(doctor.DoctorId), Day(8));
        using (var edited = await doctor.Client.PutAsJsonAsync(
            $"{BlocksUrl}/{tuesday.Id}",
            new RescheduleAvailabilityBlockRequest(Day(8), new TimeOnly(9, 0), new TimeOnly(15, 0))))
        {
            await edited.ShouldHaveStatusAsync(HttpStatusCode.OK);
        }

        using var response = await doctor.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule());

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var blocks = await ReadBlocksAsync(doctor.DoctorId);
        AssertBlock(blocks, Day(8), 9, 15);
        BlockOn(blocks, Day(8)).IsModified.ShouldBeTrue();
        AssertBlock(blocks, Day(15), 10, 16);
    }

    [Fact]
    public async Task Un_jueves_la_semana_siguiente_tampoco_cambia()
    {
        var doctor = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Thursday);
        var seriesId = await CreateSeriesAsync(doctor);

        using var response = await doctor.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule());

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<UpdateAvailabilitySeriesResponse>())!.EffectiveFrom.ShouldBe(Day(14));
        var blocks = await ReadBlocksAsync(doctor.DoctorId);
        AssertBlock(blocks, Day(7), 8, 14);
        AssertBlock(blocks, Day(10), 8, 14);
        AssertBlock(blocks, Day(14), 10, 16);
        blocks.ShouldNotContain(block => LocalDate(block.StartsAt) == Day(17));
    }

    [Fact]
    public async Task Cancela_con_el_motivo_las_citas_que_quedan_fuera_y_avisa_solo_a_esos_pacientes()
    {
        var doctor = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        var seriesId = await CreateSeriesAsync(doctor);
        var early = await SeedPatientAsync();
        var removedDay = await SeedPatientAsync();
        var inside = await SeedPatientAsync();
        var earlyAppointment = await InsertAppointmentAsync(early, doctor, Local(Day(7), 8));
        var removedDayAppointment = await InsertAppointmentAsync(removedDay, doctor, Local(Day(10), 8));
        var insideAppointment = await InsertAppointmentAsync(inside, doctor, Local(Day(7), 12));

        using var response = await doctor.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule(Reason));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<UpdateAvailabilitySeriesResponse>())
            .ShouldBe(new UpdateAvailabilitySeriesResponse(Day(7), 2));
        (await ReadStateAsync(earlyAppointment)).ShouldBe(new AppointmentState(Cancelled, Reason));
        (await ReadStateAsync(removedDayAppointment)).ShouldBe(new AppointmentState(Cancelled, Reason));
        (await ReadStateAsync(insideAppointment)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>()
            .Select(notification => notification.Recipient.Email)
            .ShouldBe(new[] { early.Email, removedDay.Email }, ignoreOrder: true);
    }

    [Fact]
    public async Task Con_citas_fuera_y_sin_motivo_se_rechaza_sin_cambiar_la_serie_ni_los_bloques()
    {
        var doctor = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        var seriesId = await CreateSeriesAsync(doctor);
        var appointment = await InsertAppointmentAsync(await SeedPatientAsync(), doctor, Local(Day(10), 8));

        using var response = await doctor.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule());

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonRequired");
        (await ReadStateAsync(appointment)).ShouldBe(new AppointmentState(Pending, null));
        var blocks = await ReadBlocksAsync(doctor.DoctorId);
        AssertBlock(blocks, Day(7), 8, 14);
        AssertBlock(blocks, Day(10), 8, 14);
        blocks.ShouldNotContain(block => LocalDate(block.StartsAt) == Day(9));
        (await ReadStartDateAsync(seriesId)).ShouldBe(Day(1));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_fecha_hasta_pasada_se_rechaza()
    {
        var doctor = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        var seriesId = await CreateSeriesAsync(doctor);

        using var response = await doctor.Client.PutAsJsonAsync(
            $"{SeriesUrl}/{seriesId}", NewSchedule() with { EndDate = Day(0) });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "AvailabilitySeries.InvalidDateRange");
        AssertBlock(await ReadBlocksAsync(doctor.DoctorId), Day(7), 8, 14);
    }

    [Fact]
    public async Task Un_medico_no_puede_editar_la_serie_de_otro_y_un_paciente_no_puede_editar_series()
    {
        var owner = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        var seriesId = await CreateSeriesAsync(owner);

        using var byOther = await other.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule());
        using var byPatient = await patient.Client.PutAsJsonAsync($"{SeriesUrl}/{seriesId}", NewSchedule());

        await AssertErrorAsync(byOther, HttpStatusCode.NotFound, "AvailabilitySeries.NotFound");
        await byPatient.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        AssertBlock(await ReadBlocksAsync(owner.DoctorId), Day(7), 8, 14);
    }

    [Fact]
    public async Task El_medico_consulta_sus_bloques_de_hasta_31_dias()
    {
        var doctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        MoveClockTo(DayOfWeek.Tuesday);
        await CreateSeriesAsync(doctor);
        await CreateSeriesAsync(other);

        using var week = await doctor.Client.GetAsync($"{BlocksUrl}?from={Day(7):yyyy-MM-dd}&to={Day(13):yyyy-MM-dd}");
        using var month = await doctor.Client.GetAsync($"{BlocksUrl}?from={Day(7):yyyy-MM-dd}&to={Day(37):yyyy-MM-dd}");
        using var tooLong = await doctor.Client.GetAsync($"{BlocksUrl}?from={Day(7):yyyy-MM-dd}&to={Day(38):yyyy-MM-dd}");

        await week.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var items = (await week.Content.ReadFromJsonAsync<AvailabilityBlocksResponse>())!.Items;
        items.Select(block => LocalDate(block.StartsAt)).ShouldBe(new[] { Day(7), Day(8), Day(10) });
        var own = await ReadBlocksAsync(doctor.DoctorId);
        items.ShouldAllBe(block => own.Any(row => row.Id == block.Id));
        // Un martes la semana siguiente todavía se reprograma; la actual solo se puede cerrar.
        items.ShouldAllBe(block => block.CanReschedule);
        await month.ShouldHaveStatusAsync(HttpStatusCode.OK);
        await AssertErrorAsync(tooLong, HttpStatusCode.BadRequest, "Availability.DateRangeTooLong");

        using var currentWeek = await doctor.Client.GetAsync($"{BlocksUrl}?from={Day(0):yyyy-MM-dd}&to={Day(6):yyyy-MM-dd}");
        var current = (await currentWeek.Content.ReadFromJsonAsync<AvailabilityBlocksResponse>())!.Items;
        current.Select(block => LocalDate(block.StartsAt)).ShouldBe(new[] { Day(1), Day(3) });
        current.ShouldAllBe(block => !block.CanReschedule);
    }

    private static UpdateAvailabilitySeriesRequest NewSchedule(string? reason = null) =>
        new(MondayTuesdayWednesday, new TimeOnly(10, 0), new TimeOnly(16, 0), null, reason);

    private async Task<int> CreateSeriesAsync(DoctorSeed doctor)
    {
        using var response = await doctor.Client.PostAsJsonAsync(SeriesUrl, new CreateAvailabilitySeriesRequest(
            MondayTuesdayThursday, new TimeOnly(8, 0), new TimeOnly(14, 0), Today(), null));
        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<CreateAvailabilitySeriesResponse>())!.Id;
    }

    // Fija el reloj en el próximo martes o jueves a las 10:00 de Santo Domingo, después de iniciar sesión y menos de una
    // semana por delante, para que la sesión siga activa y la base no vea fechas en el pasado.
    private void MoveClockTo(DayOfWeek dayOfWeek)
    {
        var realNow = DateTimeOffset.UtcNow;
        var day = DateOnly.FromDateTime(realNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);
        while (day.DayOfWeek != dayOfWeek || Local(day, AppointmentStartHour) <= realNow)
            day = day.AddDays(1);
        Clock.UtcNow = Local(day, AppointmentStartHour).ToUniversalTime();
        _monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
    }

    // Días contados desde el lunes de la semana actual: 7 es el lunes siguiente.
    private DateOnly Day(int daysFromMonday) => _monday.AddDays(daysFromMonday);

    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    private static BlockRow BlockOn(IEnumerable<BlockRow> blocks, DateOnly day) =>
        blocks.Single(block => LocalDate(block.StartsAt) == day);

    private static void AssertBlock(IEnumerable<BlockRow> blocks, DateOnly day, int startHour, int endHour)
    {
        var block = BlockOn(blocks, day);
        block.StartsAt.ShouldBe(Local(day, startHour));
        block.EndsAt.ShouldBe(Local(day, endHour));
        block.IsAvailable.ShouldBeTrue();
    }

    private async Task<int> InsertAppointmentAsync(PatientSeed patient, DoctorSeed doctor, DateTimeOffset startsAt)
    {
        var id = 0;
        await WithContextAsync(async context => id = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAt.ToUniversalTime(), AppointmentHours, Pending));
        return id;
    }

    // Las fechas vuelven en UTC; la igualdad de DateTimeOffset compara el instante.
    private async Task<List<BlockRow>> ReadBlocksAsync(int doctorId)
    {
        var rows = new List<BlockRow>();
        await WithContextAsync(async context => rows = await context.Database.SqlQuery<BlockRow>(
            $"""
            SELECT id, series_id, starts_at, ends_at, is_available, is_modified
            FROM scheduling.doctor_availability
            WHERE doctor_id = {doctorId}
            ORDER BY starts_at
            """).ToListAsync());
        return rows;
    }

    private async Task<DateOnly> ReadStartDateAsync(int seriesId)
    {
        var startDate = default(DateOnly);
        await WithContextAsync(async context => startDate = await context.Database.SqlQuery<DateOnly>(
            $"SELECT start_date AS \"Value\" FROM scheduling.doctor_availability_series WHERE id = {seriesId}").SingleAsync());
        return startDate;
    }
}
