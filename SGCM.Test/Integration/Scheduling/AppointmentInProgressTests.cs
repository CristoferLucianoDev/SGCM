using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CompleteFinishedAppointments;
using SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>
/// Pruebas de integración de la cita en curso contra la API y la base reales: extender, terminar antes de tiempo y
/// completado automático. Por HTTP el reloj no se mueve (la sesión del médico se creó con la hora real) y las citas se
/// siembran relativas a la hora actual; solo las pruebas que ejecutan el comando directamente lo mueven para probar
/// el borde exacto del fin.
/// </summary>
[Trait("Category", "Integration")]
public sealed class AppointmentInProgressTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string ExtendSuffix = "extend";
    private const string CompleteSuffix = "complete";
    private const string NotInProgressCode = "Appointment.NotInProgress";
    private const string ExtensionUnavailableCode = "Appointment.ExtensionUnavailable";
    private const string InvalidAdditionalHoursCode = "Appointment.InvalidAdditionalHours";
    private const string NotFoundCode = "Appointment.NotFound";
    private const int ExtraHours = 1;
    private const int NoHours = 0;
    private const int HoursSinceStartWhenInProgress = 1;
    private const int HoursSinceStartWhenFinished = 3;
    private const int BlockHoursAfterEnd = 2;
    private const int NoBlockAfterEnd = 0;
    private const int ExpectedCompletedCount = 1;
    private const int NothingCompleted = 0;

    [Fact]
    public async Task Extender_una_cita_en_curso_dentro_del_bloque_avanza_el_fin_las_horas_pedidas()
    {
        var (doctor, appointmentId, endsAt) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);

        using var response = await SendAsync(doctor.Client, appointmentId, ExtendSuffix);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt.AddHours(ExtraHours));
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
    }

    [Fact]
    public async Task Extender_contra_otra_cita_que_empieza_en_el_fin_actual_se_rechaza_y_el_fin_no_cambia()
    {
        var (doctor, appointmentId, endsAt) = await SeedInProgressAsync(blockHoursAfterEnd: AppointmentHours);
        var other = await SeedPatientAsync();
        await InsertAppointmentAsync(doctor, other, Confirmed, endsAt);

        using var response = await SendAsync(doctor.Client, appointmentId, ExtendSuffix);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ExtensionUnavailableCode);
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt);
    }

    [Fact]
    public async Task Extender_fuera_del_bloque_se_rechaza_y_el_fin_no_cambia()
    {
        var (doctor, appointmentId, endsAt) = await SeedInProgressAsync(blockHoursAfterEnd: NoBlockAfterEnd);

        using var response = await SendAsync(doctor.Client, appointmentId, ExtendSuffix);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, ExtensionUnavailableCode);
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt);
    }

    [Fact]
    public async Task Extender_una_cita_futura_es_NotInProgress()
    {
        var startsAt = InTimeStart();
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        using var response = await SendAsync(doctor.Client, appointmentId, ExtendSuffix);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, NotInProgressCode);
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(startsAt.AddHours(AppointmentHours));
    }

    [Theory]
    [InlineData(NoHours)]
    [InlineData(ExtendAppointmentCommandValidator.MaximumAdditionalHours + 1)]
    public async Task Extender_con_horas_fuera_del_rango_responde_400_y_el_fin_no_cambia(int additionalHours)
    {
        var (doctor, appointmentId, endsAt) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);

        using var response = await SendAsync(doctor.Client, appointmentId, ExtendSuffix, additionalHours);

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, InvalidAdditionalHoursCode);
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt);
    }

    [Fact]
    public async Task Terminar_una_cita_en_curso_la_deja_completada_con_la_hora_real_de_fin_y_sin_correos()
    {
        var (doctor, appointmentId, _) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);

        using var response = await SendAsync(doctor.Client, appointmentId, CompleteSuffix);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Completed, null));
        // PostgreSQL guarda microsegundos: el reloj de la app se trunca antes de compararlo.
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(TruncateToMicroseconds(Clock.UtcNow));
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Terminar_una_cita_futura_es_NotInProgress()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());

        using var response = await SendAsync(doctor.Client, appointmentId, CompleteSuffix);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, NotInProgressCode);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
    }

    [Fact]
    public async Task Terminar_una_cita_pendiente_es_NotInProgress()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InProgressStart());

        using var response = await SendAsync(doctor.Client, appointmentId, CompleteSuffix);

        await AssertErrorAsync(response, HttpStatusCode.Conflict, NotInProgressCode);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Theory]
    [InlineData(ExtendSuffix)]
    [InlineData(CompleteSuffix)]
    public async Task Una_cita_de_otro_medico_responde_404_y_no_cambia(string suffix)
    {
        var (_, appointmentId, endsAt) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);
        var otherDoctor = await SeedDoctorAsync();

        using var response = await SendAsync(otherDoctor.Client, appointmentId, suffix);

        await AssertErrorAsync(response, HttpStatusCode.NotFound, NotFoundCode);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt);
    }

    [Theory]
    [InlineData(ExtendSuffix)]
    [InlineData(CompleteSuffix)]
    public async Task Un_paciente_autenticado_recibe_403(string suffix)
    {
        var (_, appointmentId, endsAt) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);
        var patient = await SeedPatientAsync();

        using var response = await SendAsync(patient.Client, appointmentId, suffix);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt);
    }

    [Theory]
    [InlineData(ExtendSuffix)]
    [InlineData(CompleteSuffix)]
    public async Task Sin_token_responde_401(string suffix)
    {
        var (_, appointmentId, _) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);
        using var anonymous = Factory.CreateClient();

        using var response = await SendAsync(anonymous, appointmentId, suffix);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task El_completado_automatico_solo_completa_la_confirmada_terminada_y_conserva_su_fin()
    {
        var finishedStart = FinishedStart();
        var (_, _, finishedId) = await SeedOwnAppointmentAsync(Confirmed, finishedStart);
        var (_, _, inProgressId) = await SeedOwnAppointmentAsync(Confirmed, InProgressStart());
        var (_, _, pendingId) = await SeedOwnAppointmentAsync(Pending, finishedStart);

        var completed = await CompleteFinishedAsync();

        completed.ShouldBe(ExpectedCompletedCount);
        (await ReadStateAsync(finishedId)).ShouldBe(new AppointmentState(Completed, null));
        (await ReadEndsAtAsync(finishedId)).ShouldBe(finishedStart.AddHours(AppointmentHours));
        (await ReadStateAsync(inProgressId)).ShouldBe(new AppointmentState(Confirmed, null));
        (await ReadStateAsync(pendingId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task El_completado_automatico_respeta_el_borde_exacto_del_fin()
    {
        var startsAt = InProgressStart();
        var endsAt = startsAt.AddHours(AppointmentHours);
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, startsAt);

        Clock.UtcNow = endsAt.AddSeconds(-1);
        var oneSecondBefore = await CompleteFinishedAsync();
        var stateBefore = await ReadStateAsync(appointmentId);
        Clock.UtcNow = endsAt;
        var atTheEnd = await CompleteFinishedAsync();

        oneSecondBefore.ShouldBe(NothingCompleted);
        stateBefore.ShouldBe(new AppointmentState(Confirmed, null));
        atTheEnd.ShouldBe(ExpectedCompletedCount);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Completed, null));
        (await ReadEndsAtAsync(appointmentId)).ShouldBe(endsAt);
    }

    [Fact]
    public async Task Ejecutar_el_completado_automatico_dos_veces_completa_la_cita_una_sola_vez()
    {
        await SeedOwnAppointmentAsync(Confirmed, FinishedStart());

        var first = await CompleteFinishedAsync();
        var second = await CompleteFinishedAsync();

        first.ShouldBe(ExpectedCompletedCount);
        second.ShouldBe(NothingCompleted);
    }

    [Fact]
    public async Task Una_cita_extendida_por_el_medico_no_se_completa_al_llegar_su_fin_original()
    {
        var (doctor, appointmentId, originalEnd) = await SeedInProgressAsync(blockHoursAfterEnd: BlockHoursAfterEnd);
        using (var extend = await SendAsync(doctor.Client, appointmentId, ExtendSuffix))
        {
            await extend.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        // Ya no hay más llamadas HTTP: el reloj puede avanzar sin invalidar la sesión del médico.
        Clock.UtcNow = originalEnd;
        var completed = await CompleteFinishedAsync();

        completed.ShouldBe(NothingCompleted);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Confirmed, null));
    }

    /// <summary>Empieza hace una hora y termina en una hora, relativo a la hora en punto actual del reloj de la app.</summary>
    private DateTimeOffset InProgressStart() => CurrentHourUtc().AddHours(-HoursSinceStartWhenInProgress);

    /// <summary>Empezó hace tres horas y terminó hace una, relativo a la hora en punto actual del reloj de la app.</summary>
    private DateTimeOffset FinishedStart() => CurrentHourUtc().AddHours(-HoursSinceStartWhenFinished);

    /// <summary>
    /// Médico con una cita confirmada en curso y un bloque que llega hasta <paramref name="blockHoursAfterEnd"/> horas
    /// después del fin de la cita.
    /// </summary>
    private async Task<(DoctorSeed Doctor, int AppointmentId, DateTimeOffset EndsAt)> SeedInProgressAsync(
        int blockHoursAfterEnd)
    {
        var startsAt = InProgressStart();
        var endsAt = startsAt.AddHours(AppointmentHours);
        var doctor = await SeedDoctorAsync();
        await InsertBlockAsync(doctor.DoctorId, startsAt.AddHours(-BlockMarginHours), endsAt.AddHours(blockHoursAfterEnd));
        var appointmentId = await InsertAppointmentAsync(doctor, await SeedPatientAsync(), Confirmed, startsAt);

        return (doctor, appointmentId, endsAt);
    }

    private async Task<int> InsertAppointmentAsync(
        DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc)
    {
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status));

        return appointmentId;
    }

    private static Task<HttpResponseMessage> SendAsync(
        HttpClient client, int appointmentId, string suffix, int additionalHours = ExtraHours)
    {
        var url = $"{DoctorAppointmentsUrl}/{appointmentId}/{suffix}";

        return suffix == ExtendSuffix
            ? client.PostAsJsonAsync(url, new { additionalHours })
            : client.PostAsync(url, content: null);
    }

    private async Task<DateTimeOffset> ReadEndsAtAsync(int appointmentId)
    {
        var endsAt = default(DateTimeOffset);
        await WithContextAsync(async context => endsAt = await context.Database.SqlQuery<DateTimeOffset>(
            $"SELECT ends_at AS \"Value\" FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

        return endsAt;
    }

    /// <summary>
    /// Ejecuta el comando como el proceso automático: en un scope propio y con sus decoradores, que guardan en una
    /// transacción y publican los eventos después del commit.
    /// </summary>
    private async Task<int> CompleteFinishedAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CompleteFinishedAppointmentsCommand, int>>();

        var result = await handler.HandleAsync(new CompleteFinishedAppointmentsCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.UtcTicks - value.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
}
