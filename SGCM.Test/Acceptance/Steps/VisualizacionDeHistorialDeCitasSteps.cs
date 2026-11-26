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
public sealed class VisualizacionDeHistorialDeCitasSteps(ApiScenario scenario) : IDisposable
{
    private const string PatientAppointmentsUrl = "/api/patients/me/appointments";
    private const string DateFormat = "yyyy-MM-dd";
    private const string TimeFormat = "HH:mm";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";
    private const int AppointmentHours = 2;
    private const int SantoDomingoOffsetHours = -4;

    private sealed record DoctorSeed(int DoctorId, int SpecialtyId);

    private sealed record SeededAppointment(int Id, DateTimeOffset StartsAt);

    // Los estados del esquema se escriben en español; la base los guarda en inglés.
    private static readonly Dictionary<string, string> StatusBySpanishName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pendiente"] = Pending,
        ["confirmada"] = Confirmed,
        ["rechazada"] = Rejected,
        ["cancelada"] = Cancelled,
        ["completada"] = Completed
    };

    private readonly List<SeededAppointment> _own = [];
    private int _patientUserId;
    private DoctorSeed? _doctor;
    private int _othersAppointmentId;
    private ListPatientAppointmentsResponse? _list;
    private PatientAppointmentDetailResponse? _detail;
    private JsonDocument? _problem;

    [Given("que inicié sesión como paciente para revisar mi historial de citas")]
    public Task DadoQueInicieSesionComoPaciente() =>
        AuthenticatedScenario.LoginAsync(scenario, "Patient", "historial-paciente", id => _patientUserId = id);

    [Given("que en mi historial hay una cita {string} de hace {int} día(s) a las {string}")]
    public async Task DadoQueHayUnaCitaPasada(string status, int days, string time) =>
        await SeedOwnAppointmentAsync(status, -days, time, statusReason: null);

    [Given("que en mi historial hay una cita {string} para dentro de {int} día(s) a las {string}")]
    public async Task DadoQueHayUnaCitaFutura(string status, int days, string time) =>
        await SeedOwnAppointmentAsync(status, days, time, statusReason: null);

    // Los dos motivos se guardan al sembrar la cita: una cita en estado final ya no admite modificaciones.
    [Given("que en mi historial hay una cita {string} para dentro de {int} día(s) a las {string} con motivo de consulta {string} y motivo de estado {string}")]
    public async Task DadoQueHayUnaCitaConMotivos(
        string status, int days, string time, string reasonForVisit, string statusReason) =>
        await SeedOwnAppointmentAsync(status, days, time, statusReason, reasonForVisit);

    [Given("que otro paciente tiene en su historial una cita {string} para dentro de {int} día(s) a las {string}")]
    public async Task DadoQueOtroPacienteTieneUnaCita(string status, int days, string time)
    {
        var email = $"historial-ajeno-{Guid.NewGuid():N}@correo.com";
        var other = UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(other);
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, other.Id);
            return 0;
        });
        var seeded = await SeedAppointmentAsync(
            await PatientIdAsync(other.Id), status, StartsAtUtc(days, time), statusReason: null);
        _othersAppointmentId = seeded.Id;
    }

    [When("reviso mi historial completo de citas")]
    public Task CuandoRevisoMiHistorial() => GetListAsync(string.Empty);

    [When("reviso mi historial de citas filtrando por el estado {string}")]
    public Task CuandoRevisoMiHistorialPorEstado(string status) =>
        GetListAsync($"?status={StatusBySpanishName[status]}");

    [When("reviso mi historial de citas entre el día {int} y el día {int} desde hoy")]
    public Task CuandoRevisoMiHistorialPorFechas(int fromDays, int toDays) =>
        GetListAsync($"?from={FormatDate(TodayLocal().AddDays(fromDays))}&to={FormatDate(TodayLocal().AddDays(toDays))}");

    [When("reviso la página {int} de mi historial con {int} citas por página")]
    public Task CuandoRevisoUnaPagina(int pageNumber, int pageSize) =>
        GetListAsync($"?pageNumber={pageNumber}&pageSize={pageSize}");

    [When("abro el detalle de esa cita en mi historial")]
    public Task CuandoAbroElDetalle() => GetDetailAsync(_own[^1].Id);

    [When("abro el detalle de la cita del otro paciente")]
    public Task CuandoAbroElDetalleAjeno() => GetDetailAsync(_othersAppointmentId);

    [Then("el listado de mi historial contiene {int} cita(s)")]
    public void EntoncesElListadoContiene(int count)
    {
        var list = List();

        list.Items.Count.ShouldBe(count);
        list.TotalCount.ShouldBe(count);
    }

    [Then("el listado ordena sus citas de la más reciente a la más antigua")]
    public void EntoncesElListadoEstaOrdenado()
    {
        var list = List();

        list.Items.Select(item => item.StartsAt).ShouldBe(list.Items.Select(item => item.StartsAt).OrderDescending());
    }

    [Then("la primera cita del listado tiene estado {string}")]
    public void EntoncesLaPrimeraCitaTieneEstado(string status) =>
        List().Items[0].Status.ShouldBe(StatusBySpanishName[status]);

    [Then("la primera cita del listado empieza a las {string} y termina a las {string}")]
    public void EntoncesLaPrimeraCitaTieneHoras(string start, string end)
    {
        var first = List().Items[0];

        first.StartTime.ShouldBe(ParseTime(start));
        first.EndTime.ShouldBe(ParseTime(end));
        first.AppointmentDate.ShouldBe(DateOnly.FromDateTime(first.StartsAt.ToOffset(LocalOffset()).DateTime));
    }

    [Then("la primera cita del listado es del médico {string} y de la especialidad sembrada")]
    public async Task EntoncesLaPrimeraCitaTieneMedicoYEspecialidad(string doctorName)
    {
        var first = List().Items[0];
        var doctor = _doctor.ShouldNotBeNull();

        first.DoctorName.ShouldBe(doctorName);
        first.DoctorId.ShouldBe(doctor.DoctorId);
        first.SpecialtyId.ShouldBe(doctor.SpecialtyId);
        first.SpecialtyName.ShouldBe(await SpecialtyNameAsync(doctor.SpecialtyId));
    }

    [Then("todas las citas del listado tienen estado {string}")]
    public void EntoncesTodasLasCitasTienenEstado(string status) =>
        List().Items.ShouldAllBe(item => item.Status == StatusBySpanishName[status]);

    [Then("el listado solo incluye las citas de los días {int} a {int} desde hoy")]
    public void EntoncesElListadoSoloIncluyeLosDias(int fromDays, int toDays)
    {
        var from = TodayLocal().AddDays(fromDays);
        var to = TodayLocal().AddDays(toDays);
        var expected = _own
            .Where(seeded => DateOnly.FromDateTime(seeded.StartsAt.ToOffset(LocalOffset()).DateTime) >= from
                && DateOnly.FromDateTime(seeded.StartsAt.ToOffset(LocalOffset()).DateTime) <= to)
            .Select(seeded => seeded.Id);

        List().Items.Select(item => item.AppointmentId).ShouldBe(expected, ignoreOrder: true);
    }

    [Then("el listado informa la página {int}, el tamaño {int} y un total de {int} cita(s)")]
    public void EntoncesElListadoInformaLaPagina(int pageNumber, int pageSize, int total)
    {
        var list = List();

        list.PageNumber.ShouldBe(pageNumber);
        list.PageSize.ShouldBe(pageSize);
        list.TotalCount.ShouldBe(total);
    }

    [Then("el listado devuelve la cita número {int} y la número {int} de mi historial ordenado de la más reciente")]
    public void EntoncesElListadoDevuelveDosCitas(int firstPosition, int secondPosition)
    {
        var newestFirst = _own.OrderByDescending(seeded => seeded.StartsAt).Select(seeded => seeded.Id).ToList();

        List().Items.Select(item => item.AppointmentId)
            .ShouldBe(new[] { newestFirst[firstPosition - 1], newestFirst[secondPosition - 1] });
    }

    [Then("el detalle de mi historial tiene estado {string}")]
    public void EntoncesElDetalleTieneEstado(string status) =>
        Detail().Appointment.Status.ShouldBe(StatusBySpanishName[status]);

    [Then("el detalle indica el motivo de consulta {string}")]
    public void EntoncesElDetalleIndicaMotivoDeConsulta(string reason) =>
        Detail().ReasonForVisit.ShouldBe(reason);

    [Then("el detalle indica el motivo de estado {string}")]
    public void EntoncesElDetalleIndicaMotivoDeEstado(string reason) =>
        Detail().StatusReason.ShouldBe(reason);

    [Then("la consulta del historial se rechaza con el código {string}")]
    public async Task EntoncesLaConsultaSeRechaza(string code)
    {
        _problem ??= JsonDocument.Parse(await scenario.Response!.Content.ReadAsStringAsync());

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(400, 499);
        _problem.RootElement.GetProperty("code").GetString().ShouldBe(code);
    }

    /// <summary>Libera el documento del último ProblemDetails; la respuesta y el cliente los libera el escenario.</summary>
    public void Dispose() => _problem?.Dispose();

    private async Task GetListAsync(string query)
    {
        await ReplaceResponseAsync(PatientAppointmentsUrl + query);
        var response = scenario.Response!;
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        _list = await response.Content.ReadFromJsonAsync<ListPatientAppointmentsResponse>();
    }

    private async Task GetDetailAsync(int appointmentId)
    {
        await ReplaceResponseAsync($"{PatientAppointmentsUrl}/{appointmentId}");
        if (scenario.Response!.StatusCode != HttpStatusCode.OK) return;

        _detail = await scenario.Response.Content.ReadFromJsonAsync<PatientAppointmentDetailResponse>();
    }

    private async Task ReplaceResponseAsync(string url)
    {
        scenario.Response?.Dispose();
        _problem?.Dispose();
        _problem = null;
        _list = null;
        _detail = null;
        scenario.Response = await scenario.Client.GetAsync(url);
    }

    private ListPatientAppointmentsResponse List() =>
        _list.ShouldNotBeNull("No se consultó el historial en este escenario.");

    private PatientAppointmentDetailResponse Detail() =>
        _detail.ShouldNotBeNull("No se obtuvo el detalle de la cita en este escenario.");

    private async Task SeedOwnAppointmentAsync(
        string spanishStatus, int days, string time, string? statusReason, string? reasonForVisit = null)
    {
        var seeded = await SeedAppointmentAsync(
            await PatientIdAsync(_patientUserId), spanishStatus, StartsAtUtc(days, time), statusReason, reasonForVisit);
        _own.Add(seeded);
    }

    /// <summary>
    /// Cita del paciente en el estado pedido, dentro de un bloque que la cubre exactamente para que los bloques del
    /// médico no se crucen. La cita pasa por transiciones válidas hasta el estado final.
    /// </summary>
    private async Task<SeededAppointment> SeedAppointmentAsync(
        int patientId, string spanishStatus, DateTimeOffset startsAtUtc, string? statusReason, string? reasonForVisit = null)
    {
        var status = StatusBySpanishName[spanishStatus];
        var doctor = await EnsureDoctorAsync();
        var endsAtUtc = startsAtUtc.AddHours(AppointmentHours);
        var appointmentId = await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({doctor.DoctorId}, NULL, {startsAtUtc}, {endsAtUtc}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status, statusReason,
                reasonForVisit);
        });

        return new SeededAppointment(appointmentId, startsAtUtc);
    }

    // Un solo médico por escenario: con la especialidad nueva y el perfil completo.
    private async Task<DoctorSeed> EnsureDoctorAsync()
    {
        if (_doctor is not null) return _doctor;

        var email = $"historial-medico-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        _doctor = await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return new DoctorSeed(doctorId, specialtyId);
        });

        return _doctor;
    }

    private Task<int> PatientIdAsync(int userId) =>
        WithContextAsync(context => context.Patients.Where(patient => patient.UserId == userId)
            .Select(patient => patient.Id).SingleAsync());

    private Task<string> SpecialtyNameAsync(int specialtyId) =>
        WithContextAsync(context => context.Specialties.Where(specialty => specialty.Id == specialtyId)
            .Select(specialty => specialty.Name).SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private DateTimeOffset StartsAtUtc(int days, string time) =>
        new DateTimeOffset(TodayLocal().AddDays(days).ToDateTime(ParseTime(time)), LocalOffset()).ToUniversalTime();

    // Hoy en Santo Domingo según el reloj de la aplicación; Santo Domingo está en UTC-4 todo el año.
    private DateOnly TodayLocal() =>
        DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(LocalOffset()).DateTime);

    private static TimeSpan LocalOffset() => TimeSpan.FromHours(SantoDomingoOffsetHours);

    private static TimeOnly ParseTime(string value) => TimeOnly.ParseExact(value, TimeFormat, CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly date) => date.ToString(DateFormat, CultureInfo.InvariantCulture);
}
