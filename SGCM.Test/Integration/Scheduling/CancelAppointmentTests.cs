using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class CancelAppointmentTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Password = "Secreta123";
    private const string AppointmentsUrl = "/api/appointments";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";
    private const string Reason = "Viaje";
    private const string RejectionReason = "Agenda completa";
    private const string EarlierCancellationReason = "Cancelada antes";
    private const string CallTheDoctorText = "llamar al médico";
    private const int AppointmentHours = 2;
    private const int DaysAhead = 10;
    private const int AppointmentStartHour = 10;
    private const int HoursPastTheDeadline = 23;
    private const int BlockMarginHours = 1;
    private const int SantoDomingoOffsetHours = -4;

    private sealed record DoctorSeed(string Email, int DoctorId, int SpecialtyId);

    private sealed record PatientSeed(string Email, int PatientId, HttpClient Client);

    private sealed record AppointmentState(string Status, string? StatusReason);

    private readonly List<HttpClient> _clients = [];

    [Theory]
    [InlineData(Pending)]
    [InlineData(Confirmed)]
    public async Task Cancelar_una_cita_propia_en_plazo_la_deja_cancelada_con_su_motivo_y_avisa_solo_al_medico(string seeded)
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, Reason));
        var notification = Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        notification.Recipient.Email.ShouldBe(doctor.Email);
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
        notification.CancellationReason.ShouldBe(Reason);
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == patient.Email);
    }

    [Fact]
    public async Task Cancelar_una_cita_confirmada_sin_motivo_guarda_el_motivo_nulo_y_lo_notifica()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = (string?)null });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Cancelled, null));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem()
            .CancellationReason.ShouldBeNull();
    }

    [Fact]
    public async Task Tras_cancelar_el_horario_queda_libre_para_otro_paciente()
    {
        var startsAt = InTimeStart();
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, startsAt);
        var other = await SeedPatientAsync();
        using (var cancel = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }))
        {
            await cancel.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        }

        using var response = await other.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt,
            doctorId = doctor.DoctorId
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Fuera_de_plazo_se_rechaza_indicando_que_debe_llamar_al_medico_y_no_avisa_a_nadie()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, PastDeadlineStart());

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        var title = await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.CancellationDeadlinePassed");
        title.ShouldContain(CallTheDoctorText);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(Rejected, RejectionReason)]
    [InlineData(Cancelled, EarlierCancellationReason)]
    [InlineData(Completed, null)]
    public async Task Una_cita_que_no_esta_activa_no_se_puede_cancelar_y_la_fila_no_cambia(string seeded, string? statusReason)
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(seeded, InTimeStart(), statusReason);

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.InvalidTransition");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(seeded, statusReason));
        Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task La_cita_de_otro_paciente_responde_404_y_no_cambia()
    {
        var (_, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var stranger = await SeedPatientAsync();

        using var response = await stranger.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Una_cita_que_no_existe_responde_404()
    {
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(int.MaxValue), new { reason = Reason });

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task Un_motivo_demasiado_largo_se_rechaza_y_la_cita_sigue_pendiente()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var reason = new string('a', Appointment.StatusReasonMaxLength + 1);

        using var response = await patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.StatusReasonTooLong");
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Un_medico_no_puede_cancelar_citas_desde_este_endpoint()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        var login = await LoginSuccessfullyAsync(doctor.Email, Password);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var response = await Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState(Pending, null));
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var response = await Client.PostAsJsonAsync(CancelUrl(1), new { reason = Reason });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Dos_cancelaciones_simultaneas_de_la_misma_cita_solo_dejan_pasar_una()
    {
        var (_, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());

        var responses = await Task.WhenAll(
            patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }),
            patient.Client.PostAsJsonAsync(CancelUrl(appointmentId), new { reason = Reason }));

        try
        {
            responses.Select(response => response.StatusCode)
                .ShouldBe(new[] { HttpStatusCode.NoContent, HttpStatusCode.Conflict }, ignoreOrder: true);
            var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await rejected.ReadErrorCodeAsync()).ShouldBe("Appointment.InvalidTransition");
            (await ReadStateAsync(appointmentId)).Status.ShouldBe(Cancelled);
            Factory.Notifications.Sent.OfType<AppointmentCancelledNotification>().ShouldHaveSingleItem();
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    // El reloj falso arranca en una fecha fija; los tokens vencen según el reloj real y el plazo se calcula con el
    // reloj de la app, así que las pruebas trabajan con la hora actual desde el principio.
    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow = DateTimeOffset.UtcNow;
    }

    public override async Task DisposeAsync()
    {
        foreach (var client in _clients) client.Dispose();
        await base.DisposeAsync();
    }

    private static string CancelUrl(int appointmentId) => $"{AppointmentsUrl}/{appointmentId}/cancel";

    /// <summary>Inicio en punto, 10 días adelante, dentro del plazo para cancelar.</summary>
    private DateTimeOffset InTimeStart() =>
        Local(Today().AddDays(DaysAhead), AppointmentStartHour).ToUniversalTime();

    /// <summary>Hora en punto actual más 23 horas: siempre queda a menos de 24 horas, fuera del plazo.</summary>
    private DateTimeOffset PastDeadlineStart()
    {
        var now = Clock.UtcNow.ToUniversalTime();
        var currentHour = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero);
        return currentHour.AddHours(HoursPastTheDeadline);
    }

    /// <summary>Médico con un bloque que contiene la cita y un paciente dueño de una cita en el estado indicado.</summary>
    private async Task<(DoctorSeed Doctor, PatientSeed Patient, int AppointmentId)> SeedOwnAppointmentAsync(
        string status, DateTimeOffset startsAtUtc, string? statusReason = null)
    {
        var doctor = await SeedDoctorAsync();
        await InsertBlockAsync(
            doctor.DoctorId, startsAtUtc.AddHours(-BlockMarginHours), startsAtUtc.AddHours(AppointmentHours + BlockMarginHours));
        var patient = await SeedPatientAsync();
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status, statusReason));

        return (doctor, patient, appointmentId);
    }

    private async Task<AppointmentState> ReadStateAsync(int appointmentId)
    {
        AppointmentState? state = null;
        await WithContextAsync(async context => state = await context.Database.SqlQuery<AppointmentState>(
            $"""
            SELECT status, status_reason
            FROM scheduling.appointment
            WHERE id = {appointmentId}
            """).SingleAsync());
        return state!;
    }

    // Médico activo con perfil completo y una especialidad nueva activa.
    private async Task<DoctorSeed> SeedDoctorAsync()
    {
        var email = $"medico-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        DoctorSeed? seed = null;
        await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialty = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialty);
            seed = new DoctorSeed(email, doctorId, specialty);
        });

        return seed!;
    }

    /// <summary>Crea un paciente con perfil completo y un cliente propio con su sesión iniciada.</summary>
    private async Task<PatientSeed> SeedPatientAsync()
    {
        var email = $"paciente-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Patient, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        var patientId = 0;
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);
            patientId = await context.Patients.Where(patient => patient.UserId == user.Id)
                .Select(patient => patient.Id).SingleAsync();
        });

        var client = Factory.CreateClient();
        _clients.Add(client);
        using var login = await LoginAsync(email, Password, client);
        await login.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var tokens = await login.ReadLoginAsync();
        tokens.PendingStep.ShouldBeNull();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);

        return new PatientSeed(email, patientId, client);
    }

    // Npgsql solo escribe timestamptz en UTC, por eso los instantes llegan ya en UTC.
    private Task InsertBlockAsync(int doctorId, DateTimeOffset startsAtUtc, DateTimeOffset endsAtUtc) =>
        WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAtUtc}, {endsAtUtc}, true)
            """));

    /// <summary>Verifica el status y el código del ProblemDetails, y devuelve su título ya sin escapes de JSON.</summary>
    private static async Task<string> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.ShouldHaveStatusAsync(status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem.GetProperty("title").GetString()!;
    }

    // Hoy en Santo Domingo según el reloj falso de la aplicación; Santo Domingo está en UTC-4 todo el año.
    private DateOnly Today() =>
        DateOnly.FromDateTime(Clock.UtcNow.ToOffset(TimeSpan.FromHours(SantoDomingoOffsetHours)).DateTime);

    private static DateTimeOffset Local(DateOnly day, int hour) =>
        new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(SantoDomingoOffsetHours));

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
