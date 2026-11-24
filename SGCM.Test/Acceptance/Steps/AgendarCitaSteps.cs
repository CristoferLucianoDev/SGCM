using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Reqnroll;
using SGCM.Data.Application.Scheduling.Notifications;
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
public sealed class AgendarCitaSteps(ApiScenario scenario) : IDisposable
{
    private const string AppointmentsUrl = "/api/appointments";
    private const int AppointmentHours = 2;

    private sealed record DoctorSeed(int DoctorId, string Email);

    private readonly List<DoctorSeed> _doctors = [];
    private readonly List<ApiScenario> _otherPatients = [];
    private HttpResponseMessage[] _simultaneous = [];
    private int _specialtyId;
    private DateOnly _day;
    private DateTimeOffset _startsAt;
    private DoctorAssignmentResponse? _summary;
    private JsonElement? _problem;

    private DoctorSeed First => _doctors[0];

    private DoctorSeed Other => _doctors[1];

    [Given("que inicié sesión como paciente para agendar una cita")]
    public Task DadoQueInicieSesionComoPaciente() =>
        AuthenticatedScenario.LoginAsync(scenario, "Patient", "agendar-paciente");

    [Given("que existe un médico para agendar con un bloque dentro de {int} días de {string} a {string}")]
    public async Task DadoQueExisteUnMedicoParaAgendar(int days, string start, string end)
    {
        // Los tokens vencen según el reloj real, así que "hoy" se calcula con la hora actual.
        scenario.Factory.Clock.UtcNow = DateTimeOffset.UtcNow;
        _day = LocalDate(scenario.Factory.Clock.UtcNow).AddDays(days);
        _specialtyId = await WithContextAsync(InsertSpecialtyAsync);
        await SeedDoctorWithBlockAsync(Time(start), Time(end));
    }

    [Given("que existe otro médico de la misma especialidad con un bloque ese día de {string} a {string}")]
    public Task DadoQueExisteOtroMedico(string start, string end) => SeedDoctorWithBlockAsync(Time(start), Time(end));

    [Given("que el primer médico recibió otra cita ese día a las {string}")]
    public async Task DadoQueElPrimerMedicoRecibioOtraCita(string time)
    {
        // Npgsql solo escribe timestamptz en UTC; la API sí acepta el desfase de Santo Domingo.
        var startsAt = Local(_day, Time(time)).ToUniversalTime();
        var endsAt = startsAt.AddHours(AppointmentHours);
        var patient = UserMother.WithRole(UserRole.Patient, email: Email.Create($"otra-cita-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(patient);
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, patient.Id);
            var patientId = await context.Patients.Where(value => value.UserId == patient.Id)
                .Select(value => value.Id).SingleAsync();
            return await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.appointment (patient_id, doctor_id, specialty_id, starts_at, ends_at, status)
                VALUES ({patientId}, {First.DoctorId}, {_specialtyId}, {startsAt}, {endsAt}, 'pending')
                """);
        });
    }

    [Given("que ya tengo una cita con el primer médico ese día a las {string}")]
    public async Task DadoQueYaTengoUnaCita(string time)
    {
        await RequestAsync(new { specialtyId = _specialtyId, startsAt = Local(_day, Time(time)), doctorId = First.DoctorId });
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [When("solicito una cita con el primer médico ese día a las {string}")]
    public Task CuandoSolicitoConElPrimerMedico(string time) => RequestWithChosenDoctorAsync(First, time);

    [When("solicito una cita con el otro médico ese día a las {string}")]
    public Task CuandoSolicitoConElOtroMedico(string time) => RequestWithChosenDoctorAsync(Other, time);

    [When("solicito una cita con el primer médico como asignado ese día a las {string}")]
    public Task CuandoSolicitoConElPrimerMedicoComoAsignado(string time)
    {
        _startsAt = Local(_day, Time(time));
        return RequestAsync(new { specialtyId = _specialtyId, startsAt = _startsAt, assignedDoctorId = First.DoctorId });
    }

    [When("pido el resumen de la especialidad para ese día a las {string}")]
    public async Task CuandoPidoElResumen(string time)
    {
        _startsAt = Local(_day, Time(time));
        using var response = await scenario.Client.GetAsync(
            $"{AppointmentsUrl}/doctor-assignment?specialtyId={_specialtyId}&startsAt={Uri.EscapeDataString(_startsAt.ToString("O"))}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        _summary = await response.Content.ReadFromJsonAsync<DoctorAssignmentResponse>();
    }

    [When("envío la solicitud con el médico del resumen")]
    public Task CuandoEnvioLaSolicitudConElMedicoDelResumen() =>
        RequestAsync(new { specialtyId = _specialtyId, startsAt = _startsAt, assignedDoctorId = _summary!.DoctorId });

    [When("dos pacientes solicitan a la vez el primer médico ese día a las {string}")]
    public async Task CuandoDosPacientesSolicitanALaVez(string time)
    {
        _startsAt = Local(_day, Time(time));
        for (var index = 0; index < 2; index++)
        {
            var patient = new ApiScenario();
            _otherPatients.Add(patient);
            await AuthenticatedScenario.LoginAsync(patient, "Patient", "simultaneo");
        }

        var body = new { specialtyId = _specialtyId, startsAt = _startsAt, doctorId = First.DoctorId };
        _simultaneous = await Task.WhenAll(_otherPatients.Select(patient => patient.Client.PostAsJsonAsync(AppointmentsUrl, body)));
    }

    [Then("el resumen muestra al primer médico con una tarifa de {int}")]
    public void EntoncesElResumenMuestraAlPrimerMedico(int fee) =>
        _summary.ShouldBe(new DoctorAssignmentResponse(First.DoctorId, "Luis Pérez", fee));

    [Then("la cita queda pendiente de 2 horas con el primer médico")]
    public async Task EntoncesLaCitaQuedaPendienteConElPrimerMedico()
    {
        var appointment = await ReadCreatedAsync();

        appointment.DoctorId.ShouldBe(First.DoctorId);
        appointment.DoctorChanged.ShouldBeFalse();
        appointment.EndsAt.ShouldBe(appointment.StartsAt.AddHours(AppointmentHours));
        (await StatusOfAsync(appointment.AppointmentId)).ShouldBe("pending");
    }

    [Then("la cita queda pendiente con el otro médico y se informa el cambio de médico")]
    public async Task EntoncesLaCitaQuedaConElOtroMedico()
    {
        var appointment = await ReadCreatedAsync();

        appointment.DoctorId.ShouldBe(Other.DoctorId);
        appointment.DoctorChanged.ShouldBeTrue();
        (await StatusOfAsync(appointment.AppointmentId)).ShouldBe("pending");
    }

    [Then("el médico recibe la notificación de la solicitud y el paciente no recibe correo")]
    public void EntoncesElMedicoRecibeLaNotificacion()
    {
        var sent = scenario.Factory.Notifications.Sent;

        sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRequestedNotification>().Recipient.Email.ShouldBe(First.Email);
    }

    [Then("la solicitud se rechaza con el código {string}")]
    public async Task EntoncesLaSolicitudSeRechaza(string code)
    {
        var problem = await ProblemAsync();

        ((int)scenario.Response!.StatusCode).ShouldBeInRange(400, 499);
        problem.GetProperty("code").GetString().ShouldBe(code);
    }

    [Then("el mensaje indica que debo llamar al médico")]
    public async Task EntoncesElMensajeIndicaLlamarAlMedico()
    {
        var problem = await ProblemAsync();

        problem.GetProperty("title").GetString().ShouldNotBeNull().ShouldContain("llamar al médico");
    }

    [Then("una solicitud crea la cita y la otra se rechaza con el código {string}")]
    public async Task EntoncesUnaSolicitudCreaLaCita(string code)
    {
        _simultaneous.Select(response => response.StatusCode)
            .ShouldBe(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, ignoreOrder: true);
        (await _simultaneous.Single(response => response.StatusCode == HttpStatusCode.Conflict).ReadErrorCodeAsync())
            .ShouldBe(code);
    }

    [Then("el médico tiene una sola cita activa")]
    public async Task EntoncesElMedicoTieneUnaSolaCita()
    {
        var count = await WithContextAsync(context => context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE doctor_id = {First.DoctorId} AND status IN ('pending', 'confirmed')
            """).SingleAsync());

        count.ShouldBe(1);
    }

    public void Dispose()
    {
        foreach (var response in _simultaneous) response.Dispose();
        foreach (var patient in _otherPatients) patient.Dispose();
    }

    private Task RequestWithChosenDoctorAsync(DoctorSeed doctor, string time)
    {
        _startsAt = Local(_day, Time(time));
        return RequestAsync(new { specialtyId = _specialtyId, startsAt = _startsAt, doctorId = doctor.DoctorId });
    }

    private async Task RequestAsync(object body)
    {
        scenario.Response?.Dispose();
        _problem = null;
        scenario.Response = await scenario.Client.PostAsJsonAsync(AppointmentsUrl, body);
    }

    /// <summary>ProblemDetails de la última solicitud; el cuerpo de la respuesta solo se puede leer una vez.</summary>
    private async Task<JsonElement> ProblemAsync() =>
        _problem ??= await scenario.Response!.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<RequestAppointmentResponse> ReadCreatedAsync()
    {
        await scenario.Response!.ShouldHaveStatusAsync(HttpStatusCode.Created);

        return (await scenario.Response!.Content.ReadFromJsonAsync<RequestAppointmentResponse>())!;
    }

    private Task<string> StatusOfAsync(int appointmentId) =>
        WithContextAsync(context => context.Database.SqlQuery<string>(
            $"SELECT status AS \"Value\" FROM scheduling.appointment WHERE id = {appointmentId}").SingleAsync());

    // Médico activo con perfil completo, la especialidad del escenario y un bloque abierto ese día.
    private async Task SeedDoctorWithBlockAsync(TimeOnly start, TimeOnly end)
    {
        var email = $"agendar-medico-{Guid.NewGuid():N}@correo.com";
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create(email).Value);
        await scenario.Factory.SaveAsync(user);
        // Npgsql solo escribe timestamptz en UTC; la API sí acepta el desfase de Santo Domingo.
        var startsAt = Local(_day, start).ToUniversalTime();
        var endsAt = Local(_day, end).ToUniversalTime();
        var doctorId = await WithContextAsync(async context =>
        {
            var id = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, id, _specialtyId);
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({id}, NULL, {startsAt}, {endsAt}, true)
                """);
            return id;
        });
        _doctors.Add(new DoctorSeed(doctorId, email));
    }

    private static async Task<int> InsertSpecialtyAsync(AppDbContext context)
    {
        var name = $"Especialidad {Guid.NewGuid():N}";
        return (await context.Database.SqlQuery<int>(
            $"""
            INSERT INTO people.specialty (name, description)
            VALUES ({name}, 'Descripción de prueba.')
            RETURNING id AS "Value"
            """).ToListAsync()).Single();
    }

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // Santo Domingo está en UTC-4 todo el año.
    private static DateOnly LocalDate(DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(TimeSpan.FromHours(-4)).DateTime);

    private static DateTimeOffset Local(DateOnly date, TimeOnly time) =>
        new(date.ToDateTime(time), TimeSpan.FromHours(-4));

    private static TimeOnly Time(string value) => TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
}
