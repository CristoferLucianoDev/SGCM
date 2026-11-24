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
public sealed class CloseAvailabilityBlockTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string BlocksUrl = "/api/doctors/me/availability-blocks";
    private const string Reason = "Congreso médico";

    [Fact]
    public async Task Cerrar_un_bloque_sin_citas_lo_deja_sin_horarios_y_no_exige_motivo()
    {
        var doctor = await SeedDoctorAsync();
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(Today().AddDays(DaysAhead), 8), 6);

        using var response = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { });

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<CloseAvailabilityBlockResponse>()).ShouldBe(new CloseAvailabilityBlockResponse(0));
        (await IsAvailableAsync(blockId)).ShouldBeFalse();
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Se_puede_cerrar_un_bloque_de_la_semana_actual()
    {
        var doctor = await SeedDoctorAsync();
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, CurrentHourUtc().AddHours(1), 6);

        using var response = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { });

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await IsAvailableAsync(blockId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Con_citas_activas_las_cancela_con_el_motivo_y_envia_un_correo_a_cada_paciente()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(DaysAhead);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);
        await InsertBlockAsync(doctor.DoctorId, Local(day.AddDays(1), 8).ToUniversalTime(), Local(day.AddDays(1), 14).ToUniversalTime());
        var first = await SeedPatientAsync();
        var second = await SeedPatientAsync();
        var firstAppointment = await InsertAppointmentAsync(first, doctor, Local(day, 8));
        var secondAppointment = await InsertAppointmentAsync(second, doctor, Local(day, 11));
        var untouched = await InsertAppointmentAsync(first, doctor, Local(day.AddDays(1), 9));

        using var response = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<CloseAvailabilityBlockResponse>()).ShouldBe(new CloseAvailabilityBlockResponse(2));
        (await ReadStateAsync(firstAppointment)).ShouldBe(new AppointmentState(Cancelled, Reason));
        (await ReadStateAsync(secondAppointment)).ShouldBe(new AppointmentState(Cancelled, Reason));
        (await ReadStateAsync(untouched)).ShouldBe(new AppointmentState(Pending, null));
        (await IsAvailableAsync(blockId)).ShouldBeFalse();

        var sent = Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ToList();
        sent.Select(notification => notification.Recipient.Email).ShouldBe(new[] { first.Email, second.Email }, ignoreOrder: true);
        sent.ShouldAllBe(notification => notification.CancellationReason == Reason);
        Factory.Notifications.Sent.ShouldNotContain(notification => notification.Recipient.Email == doctor.Email);
    }

    [Fact]
    public async Task Cerrar_un_bloque_con_citas_activas_sin_motivo_se_rechaza_sin_cambiar_nada()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(DaysAhead);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(day, 8), 6);
        var appointment = await InsertAppointmentAsync(await SeedPatientAsync(), doctor, Local(day, 9));

        using var response = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonRequired");
        (await ReadStateAsync(appointment)).ShouldBe(new AppointmentState(Pending, null));
        (await IsAvailableAsync(blockId)).ShouldBeTrue();
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Con_una_cita_a_menos_de_6_horas_exige_un_motivo_de_al_menos_30_palabras()
    {
        var doctor = await SeedDoctorAsync();
        var blockStart = CurrentHourUtc().AddHours(1);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, blockStart, 6);
        var appointment = await InsertAppointmentAsync(await SeedPatientAsync(), doctor, blockStart.AddHours(1));

        using (var shortReason = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { reason = Reason }))
        {
            await AssertErrorAsync(shortReason, HttpStatusCode.BadRequest, "Appointment.ShortNoticeReasonTooShort");
        }
        (await IsAvailableAsync(blockId)).ShouldBeTrue();

        var longReason = string.Join(' ', Enumerable.Repeat("emergencia", 30));
        using var response = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { reason = longReason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await ReadStateAsync(appointment)).ShouldBe(new AppointmentState(Cancelled, longReason));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task El_aviso_previo_indica_cuantas_citas_se_cancelarian_y_la_advertencia()
    {
        var doctor = await SeedDoctorAsync();
        var blockStart = CurrentHourUtc().AddHours(1);
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, blockStart, 6);
        var emptyBlockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(Today().AddDays(DaysAhead), 8), 6);
        await InsertAppointmentAsync(await SeedPatientAsync(), doctor, blockStart.AddHours(1));

        using var withAppointments = await doctor.Client.GetAsync($"{BlocksUrl}/{blockId}/affected-appointments");
        using var empty = await doctor.Client.GetAsync($"{BlocksUrl}/{emptyBlockId}/affected-appointments");

        await withAppointments.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await withAppointments.Content.ReadFromJsonAsync<AffectedAppointmentsResponse>())
            .ShouldBe(new AffectedAppointmentsResponse(1, true, true, 6, 30));
        (await empty.Content.ReadFromJsonAsync<AffectedAppointmentsResponse>())
            .ShouldBe(new AffectedAppointmentsResponse(0, false, false, 6, 30));
    }

    [Fact]
    public async Task Un_medico_no_puede_cerrar_ni_consultar_bloques_de_otro_medico()
    {
        var owner = await SeedDoctorAsync();
        var other = await SeedDoctorAsync();
        var blockId = await InsertBlockReturningIdAsync(owner.DoctorId, Local(Today().AddDays(DaysAhead), 8), 6);

        using var close = await other.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { reason = Reason });
        using var affected = await other.Client.GetAsync($"{BlocksUrl}/{blockId}/affected-appointments");

        await AssertErrorAsync(close, HttpStatusCode.NotFound, "Availability.NotFound");
        await AssertErrorAsync(affected, HttpStatusCode.NotFound, "Availability.NotFound");
        (await IsAvailableAsync(blockId)).ShouldBeTrue();
    }

    [Fact]
    public async Task Un_bloque_ya_cerrado_no_se_vuelve_a_cerrar()
    {
        var doctor = await SeedDoctorAsync();
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(Today().AddDays(DaysAhead), 8), 6);
        using (var first = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { }))
        {
            await first.ShouldHaveStatusAsync(HttpStatusCode.OK);
        }

        using var again = await doctor.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { });

        await AssertErrorAsync(again, HttpStatusCode.BadRequest, "Availability.AlreadyClosed");
    }

    [Fact]
    public async Task Un_paciente_no_puede_cerrar_bloques()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var blockId = await InsertBlockReturningIdAsync(doctor.DoctorId, Local(Today().AddDays(DaysAhead), 8), 6);

        using var response = await patient.Client.PostAsJsonAsync($"{BlocksUrl}/{blockId}/close", new { });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await IsAvailableAsync(blockId)).ShouldBeTrue();
    }

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

    private async Task<bool> IsAvailableAsync(int blockId)
    {
        var available = false;
        await WithContextAsync(async context => available = await context.Database.SqlQuery<bool>(
            $"SELECT is_available AS \"Value\" FROM scheduling.doctor_availability WHERE id = {blockId}").SingleAsync());
        return available;
    }
}
