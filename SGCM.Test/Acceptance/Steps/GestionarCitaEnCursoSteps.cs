using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CompleteFinishedAppointments;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos de la cita en curso: extender, terminar antes de tiempo y completado automático. El médico inicia sesión con
/// la hora real y el reloj de la app no se mueve: las citas se siembran con <see cref="TestDataSeeder"/> relativas a
/// la hora en punto actual.
/// </summary>
[Binding]
public sealed class GestionarCitaEnCursoSteps(ApiScenario scenario) : IDisposable
{
    private const string DoctorAppointmentsUrl = "/api/doctors/me/appointments";
    private const string ExtendAction = "extend";
    private const string CompleteAction = "complete";
    private const string Confirmed = "confirmed";
    private const string Completed = "completed";
    private const int BlockMarginHours = 1;
    // Cubre la cita en curso, otra de la duración siguiente pegada a su fin y una extensión de unas horas.
    private const int BlockHoursAfterEnd = 3;
    private const int NextAppointmentHours = 2;

    // Los estados del escenario se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["confirmada"] = Confirmed,
        ["completada"] = Completed
    };

    private int _doctorId;
    private int _specialtyId;
    private int _appointmentId;
    private DateTimeOffset _endsAt;
    private int _completedCount;
    private JsonDocument? _problem;

    [Given("que inicié sesión como médico para atender citas en curso")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "en-curso-medico", id => userId = id);
        _doctorId = await WithContextAsync(context => context.Doctors.Where(doctor => doctor.UserId == userId)
            .Select(doctor => doctor.Id).SingleAsync());
        _specialtyId = await WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, specialtyId);
            return specialtyId;
        });
    }

    [Given("que estoy atendiendo una cita confirmada que empezó hace {int} hora(s) y termina dentro de {int} hora(s)")]
    public async Task DadoQueEstoyAtendiendoUnaCita(int hoursSinceStart, int hoursUntilEnd)
    {
        var startsAt = CurrentHour().AddHours(-hoursSinceStart);
        _endsAt = CurrentHour().AddHours(hoursUntilEnd);
        await InsertBlockAsync(startsAt, _endsAt);
        _appointmentId = await InsertAppointmentAsync(startsAt, _endsAt);
    }

    [Given("que mi agenda tiene otra cita confirmada que empieza justo cuando termina la que estoy atendiendo")]
    public async Task DadoQueMiAgendaTieneOtraCitaPegada()
    {
        // Comparte el bloque de la cita en curso, que ya cubre este horario.
        await InsertAppointmentAsync(_endsAt, _endsAt.AddHours(NextAppointmentHours));
    }

    [Given("que una cita confirmada de mi agenda empezó hace {int} hora(s) y terminó hace {int} hora(s) sin que yo la cerrara")]
    public async Task DadoQueUnaCitaTerminoSinCerrarse(int hoursSinceStart, int hoursSinceEnd)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(hoursSinceStart, hoursSinceEnd);
        var startsAt = CurrentHour().AddHours(-hoursSinceStart);
        _endsAt = CurrentHour().AddHours(-hoursSinceEnd);
        await InsertBlockAsync(startsAt, _endsAt);
        _appointmentId = await InsertAppointmentAsync(startsAt, _endsAt);
    }

    [When("extiendo la cita que estoy atendiendo por {int} hora(s) más")]
    public Task CuandoExtiendoLaCita(int additionalHours) =>
        SendAsync(ExtendAction, JsonContent.Create(new { additionalHours }));

    [When("termino la cita que estoy atendiendo antes de lo previsto")]
    public Task CuandoTerminoLaCita() => SendAsync(CompleteAction, content: null);

    [When("se ejecuta el proceso de completado automático de citas terminadas")]
    public async Task CuandoSeEjecutaElCompletadoAutomatico()
    {
        // Mismo recorrido que el proceso automático: un scope propio y el manejador con sus decoradores.
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CompleteFinishedAppointmentsCommand, int>>();

        var result = await handler.HandleAsync(new CompleteFinishedAppointmentsCommand(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _completedCount = result.Value;
    }

    [Then("la extensión de la cita en curso se acepta")]
    public Task EntoncesLaExtensionSeAcepta() => scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("la terminación de la cita en curso se acepta")]
    public Task EntoncesLaTerminacionSeAcepta() => scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

    [Then("la cita en curso termina {int} hora(s) después de lo previsto")]
    public async Task EntoncesLaCitaTerminaDespues(int additionalHours) =>
        (await ReadEndsAtAsync()).ShouldBe(_endsAt.AddHours(additionalHours));

    [Then("la extensión de la cita en curso se rechaza con el código {string}")]
    public async Task EntoncesLaExtensionSeRechaza(string code)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Conflict);
        _problem ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

        _problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("la cita en curso conserva su hora de fin prevista")]
    public async Task EntoncesLaCitaConservaSuFin() => (await ReadEndsAtAsync()).ShouldBe(_endsAt);

    [Then("la cita en curso queda marcada como {string}")]
    public async Task EntoncesLaCitaQuedaMarcada(string spanishStatus) =>
        (await ReadStatusAsync()).ShouldBe(StatusBySpanishName[spanishStatus]);

    [Then("la hora de fin de la cita en curso es la hora real en que la terminé")]
    public async Task EntoncesLaHoraDeFinEsLaReal()
    {
        // PostgreSQL guarda microsegundos: el reloj de la app se trunca antes de compararlo.
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        var truncated = new DateTimeOffset(now.UtcTicks - now.UtcTicks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);

        (await ReadEndsAtAsync()).ShouldBe(truncated);
    }

    [Then("el proceso de completado automático terminó {int} cita(s)")]
    public void EntoncesElProcesoTermino(int count) => _completedCount.ShouldBe(count);

    [Then("la cita terminada de mi agenda queda marcada como {string} con su fin previsto")]
    public async Task EntoncesLaCitaTerminadaQuedaMarcada(string spanishStatus)
    {
        (await ReadStatusAsync()).ShouldBe(StatusBySpanishName[spanishStatus]);
        (await ReadEndsAtAsync()).ShouldBe(_endsAt);
    }

    /// <summary>Libera el documento del último ProblemDetails; la respuesta y el cliente los libera el escenario.</summary>
    public void Dispose() => _problem?.Dispose();

    private async Task SendAsync(string action, HttpContent? content)
    {
        scenario.Response?.Dispose();
        _problem?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsync($"{DoctorAppointmentsUrl}/{_appointmentId}/{action}", content);
    }

    /// <summary>Hora en punto actual en UTC según el reloj de la app, truncada.</summary>
    private DateTimeOffset CurrentHour()
    {
        var now = scenario.Factory.Clock.UtcNow.ToUniversalTime();
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
    }

    // El bloque abierto empieza un margen antes de la cita y llega unas horas después de su fin.
    private Task InsertBlockAsync(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var blockStart = startsAt.AddHours(-BlockMarginHours);
        var blockEnd = endsAt.AddHours(BlockHoursAfterEnd);

        return WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({_doctorId}, NULL, {blockStart}, {blockEnd}, true)
            """));
    }

    /// <summary>Paciente nuevo con una cita confirmada con el médico de la sesión, dentro de su bloque.</summary>
    private async Task<int> InsertAppointmentAsync(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var email = $"en-curso-paciente-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        var durationHours = (int)(endsAt - startsAt).TotalHours;

        return await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            var patientId = await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();

            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, startsAt, durationHours, Confirmed);
        });
    }

    private Task<string> ReadStatusAsync() => WithContextAsync(context => context.Database.SqlQuery<string>(
        $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

    private Task<DateTimeOffset> ReadEndsAtAsync() => WithContextAsync(context => context.Database.SqlQuery<DateTimeOffset>(
        $"SELECT ends_at AS \"Value\" FROM scheduling.appointment WHERE id = {_appointmentId}").SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
