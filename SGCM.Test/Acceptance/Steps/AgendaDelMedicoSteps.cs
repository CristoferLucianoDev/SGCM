using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
public sealed class AgendaDelMedicoSteps(ApiScenario scenario)
{
    private const string Url = "/api/doctors/me/appointments";
    private const int AppointmentHours = 2;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private sealed record SeededAppointment(int Id, string PatientName, string PatientEmail, int DaysAhead);

    private readonly List<SeededAppointment> _appointments = [];
    private int _doctorUserId;
    private int _doctorId;
    private int _specialtyId;
    private int _appointmentId;
    private JsonElement? _problem;
    private DoctorScheduleResponse? _page;

    [Given("que inicié sesión como médico para ver mi agenda")]
    public async Task DadoQueInicieSesionComoMedico()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Doctor", "agenda", userId => _doctorUserId = userId);
        _doctorId = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"SELECT id AS \"Value\" FROM people.doctor WHERE user_id = {_doctorUserId}").SingleAsync());
        _specialtyId = await AddSpecialtyAsync(_doctorId);
    }

    [Given("que tengo una cita confirmada de {string} dentro de {int} días a las {string}")]
    public Task DadoQueTengoUnaCitaConfirmada(string patient, int days, string time) =>
        AddOwnAsync(patient, days, time, "confirmed");

    [Given("que tengo una cita pendiente de {string} dentro de {int} días a las {string}")]
    public Task DadoQueTengoUnaCitaPendiente(string patient, int days, string time) =>
        AddOwnAsync(patient, days, time, "pending");

    [Given("que tengo una cita cancelada de {string} dentro de {int} días a las {string}")]
    public Task DadoQueTengoUnaCitaCancelada(string patient, int days, string time) =>
        AddOwnAsync(patient, days, time, "cancelled");

    [Given("que tengo una cita pendiente de {string} dentro de {int} días a las {string} por {string}")]
    public Task DadoQueTengoUnaCitaConMotivo(string patient, int days, string time, string reason) =>
        AddOwnAsync(patient, days, time, "pending", reason);

    [Given("que tengo {int} citas pendientes de {string} en días seguidos desde dentro de {int} días")]
    public async Task DadoQueTengoCitasSeguidas(int count, string patient, int days)
    {
        for (var offset = 0; offset < count; offset++)
            await AddOwnAsync(patient, days + offset, "08:00", "pending");
    }

    [Given("que otro médico tiene una cita dentro de {int} días a las {string}")]
    public async Task DadoQueOtroMedicoTieneUnaCita(int days, string time)
    {
        var email = $"agenda-otro-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        var otherDoctorId = await WithContextAsync(context => TestDataSeeder.InsertDoctorProfileAsync(context, user.Id));
        var specialtyId = await AddSpecialtyAsync(otherDoctorId);
        _appointmentId = (await InsertAsync(otherDoctorId, specialtyId, "Ana Pérez", days, time, "pending", null)).Id;
    }

    [When("consulto mi agenda")]
    public Task CuandoConsultoMiAgenda() => GetAsync(Url);

    [When("consulto mi agenda con estado {string}, paciente {string} y orden {string}")]
    public Task CuandoConsultoMiAgendaConFiltros(string status, string patient, string sort) =>
        GetAsync($"{Url}?status={status}&patientName={Uri.EscapeDataString(patient)}&sort={sort}");

    [When("consulto mi calendario de {int} días desde dentro de {int} días")]
    public Task CuandoConsultoMiCalendario(int count, int days) =>
        GetAsync($"{Url}/calendar?from={Today().AddDays(days):yyyy-MM-dd}&to={Today().AddDays(days + count - 1):yyyy-MM-dd}");

    [When("abro el detalle de esa cita")]
    public Task CuandoAbroElDetalle() =>
        GetAsync($"{Url}/{(_appointments.Count > 0 ? _appointments[^1].Id : _appointmentId)}");

    [Then("veo {int} cita")]
    public async Task EntoncesVeoCitas(int count)
    {
        var page = await PageAsync();
        page.TotalCount.ShouldBe(count);
        page.Items.Count.ShouldBe(count);
    }

    [Then("la cita es de {string} a las {string} con estado {string}")]
    public async Task EntoncesLaCitaEs(string patient, string time, string status)
    {
        var item = (await PageAsync()).Items.ShouldHaveSingleItem();
        item.PatientName.ShouldBe(patient);
        item.StartTime.ShouldBe(Time(time));
        item.Status.ShouldBe(status);
        item.SpecialtyId.ShouldBe(_specialtyId);
    }

    [Then("veo las citas de {string} de dentro de {int} y {int} días en ese orden")]
    public async Task EntoncesVeoLasCitasEnOrden(string patient, int firstDays, int secondDays)
    {
        var items = (await PageAsync()).Items;

        items.Select(item => item.AppointmentDate).ShouldBe(new[] { Today().AddDays(firstDays), Today().AddDays(secondDays) });
        items.ShouldAllBe(item => item.PatientName == patient && item.Status == "confirmed");
    }

    [Then("el calendario muestra {int} citas")]
    public async Task EntoncesElCalendarioMuestra(int count)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        (await scenario.Response!.Content.ReadFromJsonAsync<DoctorCalendarResponse>())!.Items.Count.ShouldBe(count);
    }

    [Then("la consulta de la agenda se rechaza con el código {string}")]
    public async Task EntoncesLaConsultaSeRechaza(string code)
    {
        ((int)scenario.Response!.StatusCode).ShouldBe(400);
        (await ProblemAsync()).GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("el detalle muestra el motivo {string}, el teléfono y el correo del paciente")]
    public async Task EntoncesElDetalleMuestra(string reason)
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var detail = (await scenario.Response!.Content.ReadFromJsonAsync<DoctorAppointmentDetailResponse>())!;
        var seeded = _appointments[^1];

        detail.AppointmentId.ShouldBe(seeded.Id);
        detail.ReasonForVisit.ShouldBe(reason);
        detail.PatientName.ShouldBe(seeded.PatientName);
        detail.PatientPhoneNumber.ShouldBe("8095550100");
        detail.PatientEmail.ShouldBe(seeded.PatientEmail);
    }

    [Then("la cita no se encuentra")]
    public async Task EntoncesLaCitaNoSeEncuentra()
    {
        ((int)scenario.Response!.StatusCode).ShouldBe(404);
        (await ProblemAsync()).GetProperty("code").GetString().ShouldBe("Appointment.NotFound");
    }

    private async Task AddOwnAsync(string patient, int days, string time, string status, string? reason = null) =>
        _appointments.Add(await InsertAsync(_doctorId, _specialtyId, patient, days, time, status, reason));

    // Cada cita lleva su propio bloque, que la cubre exactamente, y su propio paciente.
    private async Task<SeededAppointment> InsertAsync(
        int doctorId, int specialtyId, string patientName, int days, string time, string status, string? reason)
    {
        var names = patientName.Split(' ', 2);
        var email = $"agenda-paciente-{Guid.NewGuid():N}@correo.com";
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(patient);
        var startsAt = Local(Today().AddDays(days), time).ToUniversalTime();
        var id = await WithContextAsync(async context =>
        {
            var patientId = (await context.Database.SqlQuery<int>(
                $"""
                INSERT INTO people.patient (user_id, first_name, last_name, birth_date, phone_number)
                VALUES ({patient.Id}, {names[0]}, {names[1]}, {new DateOnly(1990, 5, 20)}, '8095550100')
                RETURNING id AS "Value"
                """).ToListAsync()).Single();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctorId}, NULL, {startsAt}, {startsAt.AddHours(AppointmentHours)}, true)
                """);
            var appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctorId, specialtyId, startsAt, AppointmentHours, status,
                status == "cancelled" ? "Cancelada antes" : null);
            if (reason is not null)
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE scheduling.appointment SET reason_for_visit = {reason} WHERE id = {appointmentId}");
            return appointmentId;
        });
        return new SeededAppointment(id, patientName, email, days);
    }

    private Task<int> AddSpecialtyAsync(int doctorId) =>
        WithContextAsync(async context =>
        {
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return specialtyId;
        });

    private async Task GetAsync(string url)
    {
        scenario.Response?.Dispose();
        _problem = null;
        _page = null;
        scenario.Response = await scenario.Client.GetAsync(url);
    }

    /// <summary>Página de la última respuesta; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<DoctorScheduleResponse> PageAsync()
    {
        if (_page is not null) return _page;
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return _page = (await scenario.Response!.Content.ReadFromJsonAsync<DoctorScheduleResponse>())!;
    }

    /// <summary>ProblemDetails de la última respuesta; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    private DateOnly Today() => DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(SantoDomingo).DateTime);

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static TimeOnly Time(string time) => TimeOnly.ParseExact(time, "HH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset Local(DateOnly date, string time) => new(date.ToDateTime(Time(time)), SantoDomingo);
}
