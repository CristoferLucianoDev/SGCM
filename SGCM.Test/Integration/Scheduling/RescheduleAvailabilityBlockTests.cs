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
public sealed class RescheduleAvailabilityBlockTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string BlocksUrl = "/api/doctors/me/availability-blocks";
    private const string Reason = "Cambio de consultorio";
    private const int NextWeekWednesday = 9;
    private const int WeekAfterNextWednesday = 16;

    private sealed record BlockState(DateTimeOffset StartsAt, DateTimeOffset EndsAt, bool IsAvailable);

    [Fact]
    public async Task Un_martes_se_reprograma_un_bloque_de_la_semana_siguiente()
    {
        var doctor = await SeedDoctorAsync();
        var day = MondayOfThisWeek(Tuesday()).AddDays(NextWeekWednesday);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<RescheduleAvailabilityBlockResponse>()).ShouldBe(new RescheduleAvailabilityBlockResponse(0));
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 10), Local(day, 16), true));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_jueves_un_bloque_de_la_semana_siguiente_ya_no_se_reprograma()
    {
        var doctor = await SeedDoctorAsync();
        var day = MondayOfThisWeek(Thursday()).AddDays(NextWeekWednesday);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Scheduling.NextWeekLocked");
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 8), Local(day, 14), true));
    }

    [Fact]
    public async Task Un_bloque_de_la_semana_actual_no_se_reprograma_y_el_error_indica_que_solo_se_puede_cerrar()
    {
        var doctor = await SeedDoctorAsync();
        var friday = MondayOfThisWeek(Tuesday()).AddDays(4);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(friday, 8), 6);

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(friday, 10, 16));

        var description = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Scheduling.CurrentWeekLocked");
        description.ShouldContain("solo se puede cerrar");
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(friday, 8), Local(friday, 14), true));
    }

    [Fact]
    public async Task Cancela_con_el_motivo_las_citas_que_quedan_fuera_y_avisa_solo_a_esos_pacientes()
    {
        var doctor = await SeedDoctorAsync();
        var day = MondayOfThisWeek(Thursday()).AddDays(WeekAfterNextWednesday);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);
        var outside = await SeedPatientAsync();
        var inside = await SeedPatientAsync();
        var outsideAppointment = await InsertAppointmentAsync(outside, doctor, Local(day, 8));
        var insideAppointment = await InsertAppointmentAsync(inside, doctor, Local(day, 12));

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16, Reason));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<RescheduleAvailabilityBlockResponse>()).ShouldBe(new RescheduleAvailabilityBlockResponse(1));
        (await ReadStateAsync(outsideAppointment)).ShouldBe(new AppointmentState(Cancelled, Reason));
        (await ReadStateAsync(insideAppointment)).ShouldBe(new AppointmentState(Pending, null));
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 10), Local(day, 16), true));

        var sent = Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        sent.Recipient.Email.ShouldBe(outside.Email);
        sent.CancellationReason.ShouldBe(Reason);
    }

    [Fact]
    public async Task Con_citas_fuera_del_horario_nuevo_y_sin_motivo_se_rechaza_sin_cambiar_nada()
    {
        var doctor = await SeedDoctorAsync();
        var day = MondayOfThisWeek(Tuesday()).AddDays(WeekAfterNextWednesday);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);
        var appointment = await InsertAppointmentAsync(await SeedPatientAsync(), doctor, Local(day, 8));

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16));

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonRequired");
        (await ReadStateAsync(appointment)).ShouldBe(new AppointmentState(Pending, null));
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 8), Local(day, 14), true));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Las_citas_que_siguen_dentro_del_horario_nuevo_no_exigen_motivo()
    {
        var doctor = await SeedDoctorAsync();
        var day = MondayOfThisWeek(Tuesday()).AddDays(WeekAfterNextWednesday);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);
        var appointment = await InsertAppointmentAsync(await SeedPatientAsync(), doctor, Local(day, 10));

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 9, 15));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await ReadStateAsync(appointment)).ShouldBe(new AppointmentState(Pending, null));
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 9), Local(day, 15), true));
    }

    [Fact]
    public async Task Un_horario_nuevo_que_se_traslapa_con_otro_bloque_se_rechaza()
    {
        var doctor = await SeedDoctorAsync();
        var day = MondayOfThisWeek(Tuesday()).AddDays(WeekAfterNextWednesday);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);
        await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 15), 6);

        using var response = await doctor.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16));

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Availability.Overlap");
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 8), Local(day, 14), true));
    }

    [Fact]
    public async Task Un_medico_no_puede_reprogramar_bloques_de_otro_medico_y_un_paciente_no_puede_reprogramar()
    {
        var owner = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var day = MondayOfThisWeek(Tuesday()).AddDays(WeekAfterNextWednesday);
        var blockId = await InsertBlockReturningIdAsync(owner.DoctorId, Local(day, 8), 6);

        using var byOther = await other.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16));
        using var byPatient = await patient.Client.PutAsJsonAsync($"{BlocksUrl}/{blockId}", Request(day, 10, 16));

        await AssertErrorAsync(byOther, HttpStatusCode.NotFound, "Availability.NotFound");
        await byPatient.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await ReadBlockAsync(blockId)).ShouldBe(new BlockState(Local(day, 8), Local(day, 14), true));
    }

    private static RescheduleAvailabilityBlockRequest Request(DateOnly day, int startHour, int endHour, string? reason = null) =>
        new(day, new TimeOnly(startHour, 0), new TimeOnly(endHour, 0), reason);

    // Fija el reloj en el próximo martes o jueves a las 10:00 de Santo Domingo, después de iniciar sesión y menos de una
    // semana por delante, para que la sesión siga activa y la base no vea fechas en el pasado.
    private DateOnly Tuesday() => MoveClockTo(DayOfWeek.Tuesday);

    private DateOnly Thursday() => MoveClockTo(DayOfWeek.Thursday);

    private DateOnly MoveClockTo(DayOfWeek dayOfWeek)
    {
        var realNow = DateTimeOffset.UtcNow;
        var day = DateOnly.FromDateTime(realNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);
        while (day.DayOfWeek != dayOfWeek || Local(day, AppointmentStartHour) <= realNow)
            day = day.AddDays(1);
        Clock.UtcNow = Local(day, AppointmentStartHour).ToUniversalTime();
        return day;
    }

    private static DateOnly MondayOfThisWeek(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    private async Task<int> InsertBlockReturningIdAsync(int doctorId, DateTimeOffset startsAt, int hours)
    {
        var startsAtUtc = startsAt.ToUniversalTime();
        var endsAtUtc = startsAtUtc.AddHours(hours);
        var id = 0;
        await WithContextAsync(async context => id = (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAtUtc}, {endsAtUtc}, true)
            RETURNING id AS "Value"
            """).ToListAsync()).Single());
        return id;
    }

    private async Task<int> InsertAppointmentAsync(PatientSeed patient, DoctorSeed doctor, DateTimeOffset startsAt)
    {
        var id = 0;
        await WithContextAsync(async context => id = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAt.ToUniversalTime(), AppointmentHours, Pending));
        return id;
    }

    // Las fechas vuelven en UTC; la igualdad de DateTimeOffset compara el instante.
    private async Task<BlockState> ReadBlockAsync(int blockId)
    {
        BlockState? state = null;
        await WithContextAsync(async context => state = await context.Database.SqlQuery<BlockState>(
            $"""
            SELECT starts_at, ends_at, is_available
            FROM scheduling.doctor_availability
            WHERE id = {blockId}
            """).SingleAsync());
        return state!;
    }
}
