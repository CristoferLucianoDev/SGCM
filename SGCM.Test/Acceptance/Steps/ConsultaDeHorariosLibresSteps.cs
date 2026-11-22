using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class ConsultaDeHorariosLibresSteps(ApiScenario scenario)
{
    private const string OwnUrl = "/api/doctors/me/available-slots";
    private const string Confirmed = "confirmed";
    private const string Cancelled = "cancelled";
    private const int AppointmentHours = 2;

    private static readonly TimeZoneInfo SantoDomingo = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private int _doctorId;
    private int _specialtyId;
    private DateOnly _today;
    private DateOnly _day;

    [Given("que inicié sesión como paciente para consultar horarios libres")]
    public async Task DadoQueInicieSesionComoPaciente()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Patient", "horarios-paciente");
        _today = Today();
    }

    [Given("que inicié sesión como médico para consultar mis horarios libres")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        var userId = 0;
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "horarios-medico", id => userId = id);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        _doctorId = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Doctors
            .Where(doctor => doctor.UserId == userId).Select(doctor => doctor.Id).SingleAsync();
        _today = Today();
        _day = _today;
    }

    [Given("que existe un médico con un bloque dentro de {int} días de {string} a {string}")]
    public async Task DadoQueExisteUnMedicoConUnBloque(int days, string start, string end)
    {
        await SeedDoctorAsync();
        await InsertBlockAsync(_today.AddDays(days), Time(start), Time(end), isAvailable: true);
        _day = _today.AddDays(days);
    }

    [Given("que existe un médico con un bloque cerrado dentro de {int} días de {string} a {string}")]
    public async Task DadoQueExisteUnMedicoConUnBloqueCerrado(int days, string start, string end)
    {
        await SeedDoctorAsync();
        await InsertBlockAsync(_today.AddDays(days), Time(start), Time(end), isAvailable: false);
        _day = _today.AddDays(days);
    }

    [Given("que el médico también tiene un bloque dentro de {int} días de {string} a {string}")]
    public async Task DadoQueElMedicoTambienTieneUnBloque(int days, string start, string end)
    {
        await InsertBlockAsync(_today.AddDays(days), Time(start), Time(end), isAvailable: true);
        _day = _today.AddDays(days);
    }

    [Given("que tengo un bloque hoy de {string} a {string}")]
    public Task DadoQueTengoUnBloqueHoy(string start, string end) =>
        InsertBlockAsync(_today, Time(start), Time(end), isAvailable: true);

    [Given("que ya son las {string} de hoy")]
    public void DadoQueYaSonLas(string time) =>
        scenario.Factory.Clock.UtcNow = Utc(_today, Time(time));

    [Given("que el médico tiene una cita confirmada ese día de {string} a {string}")]
    public Task DadoQueElMedicoTieneUnaCita(string start, string end) =>
        InsertAppointmentAsync(Time(start), Time(end), Confirmed);

    [Given("que el médico tuvo una cita confirmada ese día de {string} a {string} y fue cancelada")]
    public Task DadoQueElMedicoTuvoUnaCitaCancelada(string start, string end) =>
        InsertAppointmentAsync(Time(start), Time(end), Cancelled);

    [When("consulto los horarios libres de ese médico")]
    public Task CuandoConsultoLosHorariosDeEseMedico() => GetAsync($"/api/doctors/{_doctorId}/available-slots");

    [When("consulto mis horarios libres")]
    public Task CuandoConsultoMisHorarios() => GetAsync(OwnUrl);

    [Then("recibo los horarios libres a las {string}")]
    public async Task EntoncesReciboLosHorarios(string times)
    {
        var slots = await ReadSlotsAsync();

        slots.Select(slot => slot.StartsAt).ShouldBe(Times(times).Select(time => Utc(_day, time)));
        slots.ShouldAllBe(slot => slot.EndsAt - slot.StartsAt == TimeSpan.FromHours(AppointmentHours));
    }

    [Then("no recibo horarios libres")]
    public async Task EntoncesNoReciboHorarios() => (await ReadSlotsAsync()).ShouldBeEmpty();

    [Then("solo recibo horarios del día dentro de {int} días")]
    public async Task EntoncesSoloReciboHorariosDelDia(int days)
    {
        var slots = await ReadSlotsAsync();

        slots.ShouldNotBeEmpty();
        slots.ShouldAllBe(slot => LocalDate(slot.StartsAt) == _today.AddDays(days));
    }

    private async Task GetAsync(string url)
    {
        scenario.Response?.Dispose();
        scenario.Response = await scenario.Client.GetAsync(url);
    }

    private async Task<IReadOnlyList<AvailableSlotResponse>> ReadSlotsAsync()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await scenario.Response!.Content.ReadFromJsonAsync<AvailableSlotsResponse>())!.Items;
    }

    // Médico activo con una especialidad activa asociada, que las citas exigen, y sin sesión iniciada.
    private async Task SeedDoctorAsync()
    {
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"medico-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(user);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
        var name = $"Especialidad {Guid.NewGuid():N}";
        _specialtyId = (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO people.specialty (name, description)
            VALUES ({name}, 'Descripción de prueba.')
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
        await TestDataSeeder.InsertDoctorSpecialtyAsync(context, _doctorId, _specialtyId);
    }

    private async Task InsertBlockAsync(DateOnly day, TimeOnly start, TimeOnly end, bool isAvailable)
    {
        var startsAt = Utc(day, start);
        var endsAt = Utc(day, end);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({_doctorId}, NULL, {startsAt}, {endsAt}, {isAvailable})
            """);
    }

    // La cita es de un paciente nuevo. La base solo permite crearla pendiente o confirmada, así que la cancelada
    // se crea confirmada y se cambia de estado.
    private async Task InsertAppointmentAsync(TimeOnly start, TimeOnly end, string status)
    {
        var email = $"cita-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(patient);
        var startsAt = Utc(_day, start);
        var endsAt = Utc(_day, end);
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
        var patientId = await context.Patients.Where(value => value.UserId == patient.Id)
            .Select(value => value.Id).SingleAsync();
        var id = (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO scheduling.appointment (patient_id, doctor_id, specialty_id, starts_at, ends_at, status)
            VALUES ({patientId}, {_doctorId}, {_specialtyId}, {startsAt}, {endsAt}, {Confirmed})
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
        if (status == Confirmed) return;

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE scheduling.appointment SET status = {status} WHERE id = {id}");
    }

    private DateOnly Today() => LocalDate(scenario.Factory.Clock.UtcNow);

    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, SantoDomingo).DateTime);

    private static IEnumerable<TimeOnly> Times(string values) =>
        values.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(Time).ToList();

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);

    // Santo Domingo está en UTC-4 todo el año.
    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(-4)).ToUniversalTime();
}
