using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class RequestAppointmentTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Password = "Secreta123";
    private const string AppointmentsUrl = "/api/appointments";
    private const int AppointmentHours = 2;

    private sealed record DoctorSeed(string Email, int DoctorId, int SpecialtyId, int ProvinceId, int MunicipalityId);

    private sealed record PatientSeed(string Email, int PatientId, HttpClient Client);

    [Fact]
    public async Task Con_medico_elegido_crea_una_cita_pendiente_de_2_horas_y_avisa_solo_al_medico()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(day, 9),
            doctorId = doctor.DoctorId,
            reasonForVisit = "Control anual"
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RequestAppointmentResponse>())!;
        body.DoctorId.ShouldBe(doctor.DoctorId);
        body.DoctorName.ShouldBe("Luis Pérez");
        body.ConsultationFee.ShouldBe(1500m);
        body.StartsAt.ShouldBe(Local(day, 9));
        body.EndsAt.ShouldBe(Local(day, 9 + AppointmentHours));
        body.DoctorChanged.ShouldBeFalse();

        var saved = await ReadAppointmentAsync(body.AppointmentId);
        saved.ShouldBe(new AppointmentRow(patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, "pending", "Control anual"));

        var notification = Factory.Notifications.Sent.OfType<AppointmentRequestedNotification>().ShouldHaveSingleItem();
        notification.Recipient.Email.ShouldBe(doctor.Email);
        notification.Appointment.AppointmentId.ShouldBe(body.AppointmentId);
        notification.Appointment.StartTime.ShouldBe(new TimeOnly(9, 0));
        Factory.Notifications.Sent.ShouldNotContain(sent => sent.Recipient.Email == patient.Email);
    }

    [Fact]
    public async Task Solo_con_la_especialidad_el_resumen_muestra_el_medico_y_su_tarifa_y_la_solicitud_lo_conserva()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        var patient = await SeedPatientAsync();

        using var summary = await patient.Client.GetAsync(AssignmentUrl(doctor.SpecialtyId, Local(day, 10)));
        await summary.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var assignment = (await summary.Content.ReadFromJsonAsync<DoctorAssignmentResponse>())!;
        assignment.ShouldBe(new DoctorAssignmentResponse(doctor.DoctorId, "Luis Pérez", 1500m));

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(day, 10),
            assignedDoctorId = assignment.DoctorId
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RequestAppointmentResponse>())!;
        body.DoctorId.ShouldBe(doctor.DoctorId);
        body.DoctorChanged.ShouldBeFalse();
    }

    [Fact]
    public async Task El_sistema_asigna_al_azar_solo_entre_los_medicos_activos_de_la_especialidad_con_el_horario_libre()
    {
        var first = await SeedDoctorAsync();
        var second = await SeedDoctorAsync(specialtyId: first.SpecialtyId);
        var busy = await SeedDoctorAsync(specialtyId: first.SpecialtyId);
        var inactive = await SeedDoctorAsync(specialtyId: first.SpecialtyId);
        var day = Today().AddDays(10);
        foreach (var doctor in new[] { first, second, busy, inactive })
            await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        await InsertAppointmentAsync(busy, day, 10);
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET is_active = false WHERE id = {inactive.DoctorId}"));
        var patient = await SeedPatientAsync();

        var assigned = new HashSet<int>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var summary = await patient.Client.GetAsync(AssignmentUrl(first.SpecialtyId, Local(day, 10)));
            await summary.ShouldHaveStatusAsync(HttpStatusCode.OK);
            assigned.Add((await summary.Content.ReadFromJsonAsync<DoctorAssignmentResponse>())!.DoctorId);
        }

        assigned.ShouldBeSubsetOf(new[] { first.DoctorId, second.DoctorId });
    }

    [Fact]
    public async Task La_asignacion_respeta_los_filtros_de_provincia_y_municipio()
    {
        var inFilter = await SeedDoctorAsync();
        var outsideFilter = await SeedDoctorAsync(specialtyId: inFilter.SpecialtyId);
        await MoveDoctorToAnotherMunicipalityAsync(outsideFilter, inFilter);
        var day = Today().AddDays(10);
        await InsertBlockAsync(inFilter.DoctorId, day, 8, 14);
        await InsertBlockAsync(outsideFilter.DoctorId, day, 8, 14);
        var patient = await SeedPatientAsync();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var summary = await patient.Client.GetAsync(
                AssignmentUrl(inFilter.SpecialtyId, Local(day, 10), inFilter.ProvinceId, inFilter.MunicipalityId));
            (await summary.Content.ReadFromJsonAsync<DoctorAssignmentResponse>())!.DoctorId.ShouldBe(inFilter.DoctorId);
        }
    }

    [Fact]
    public async Task Si_el_medico_asignado_pierde_el_horario_antes_de_enviar_se_asigna_otro_libre_y_se_informa()
    {
        var assignedDoctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync(specialtyId: assignedDoctor.SpecialtyId);
        var day = Today().AddDays(10);
        await InsertBlockAsync(assignedDoctor.DoctorId, day, 8, 14);
        await InsertBlockAsync(other.DoctorId, day, 8, 14);
        await InsertAppointmentAsync(assignedDoctor, day, 10);
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = assignedDoctor.SpecialtyId,
            startsAt = Local(day, 10),
            assignedDoctorId = assignedDoctor.DoctorId
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Created);
        var body = (await response.Content.ReadFromJsonAsync<RequestAppointmentResponse>())!;
        body.DoctorId.ShouldBe(other.DoctorId);
        body.DoctorChanged.ShouldBeTrue();
    }

    [Fact]
    public async Task Si_el_medico_asignado_pierde_el_horario_y_no_queda_ninguno_libre_se_rechaza_con_horario_no_disponible()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        await InsertAppointmentAsync(doctor, day, 10);
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(day, 10),
            assignedDoctorId = doctor.DoctorId
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.SlotUnavailable");
    }

    [Fact]
    public async Task Si_el_paciente_eligio_al_medico_y_otro_reservo_ese_horario_antes_se_rechaza()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        await InsertAppointmentAsync(doctor, day, 10);
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(day, 11),
            doctorId = doctor.DoctorId
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.SlotUnavailable");
        Factory.Notifications.Sent.OfType<AppointmentRequestedNotification>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_horario_a_menos_de_4_dias_se_rechaza_indicando_que_debe_llamar_al_medico()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(3);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        var patient = await SeedPatientAsync();

        using var summary = await patient.Client.GetAsync(AssignmentUrl(doctor.SpecialtyId, Local(day, 10)));
        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(day, 10),
            doctorId = doctor.DoctorId
        });

        await AssertErrorAsync(summary, HttpStatusCode.BadRequest, "Appointment.TooSoon");
        var title = await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.TooSoon");
        title.ShouldContain("llamar al médico");
    }

    [Fact]
    public async Task Se_rechaza_si_se_traslapa_con_otra_cita_activa_del_paciente()
    {
        var first = await SeedDoctorAsync();
        var second = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(first.DoctorId, day, 8, 14);
        await InsertBlockAsync(second.DoctorId, day, 8, 14);
        var patient = await SeedPatientAsync();
        using (var own = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
               {
                   specialtyId = first.SpecialtyId,
                   startsAt = Local(day, 9),
                   doctorId = first.DoctorId
               }))
        {
            await own.ShouldHaveStatusAsync(HttpStatusCode.Created);
        }

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = second.SpecialtyId,
            startsAt = Local(day, 10),
            doctorId = second.DoctorId
        });

        await AssertErrorAsync(response, HttpStatusCode.Conflict, "Appointment.PatientOverlap");
    }

    [Fact]
    public async Task Se_rechaza_si_la_especialidad_no_es_una_del_medico_elegido()
    {
        var doctor = await SeedDoctorAsync();
        var otherSpecialty = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        var patient = await SeedPatientAsync();

        using var response = await patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = otherSpecialty.SpecialtyId,
            startsAt = Local(day, 10),
            doctorId = doctor.DoctorId
        });

        await AssertErrorAsync(response, HttpStatusCode.BadRequest, "Appointment.DoctorNotInSpecialty");
    }

    [Fact]
    public async Task Dos_reservas_simultaneas_del_mismo_horario_con_el_medico_elegido_solo_dejan_pasar_una()
    {
        var doctor = await SeedDoctorAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, 8, 14);
        var patients = new[] { await SeedPatientAsync(), await SeedPatientAsync() };

        var responses = await Task.WhenAll(patients.Select(patient => patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(day, 10),
            doctorId = doctor.DoctorId
        })));

        try
        {
            responses.Select(response => response.StatusCode)
                .ShouldBe(new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }, ignoreOrder: true);
            var rejected = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await rejected.ReadErrorCodeAsync()).ShouldBe("Appointment.SlotUnavailable");
            (await CountActiveAppointmentsAsync(doctor.DoctorId)).ShouldBe(1);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Dos_reservas_simultaneas_con_el_mismo_medico_asignado_reasignan_a_la_segunda_si_hay_otro_libre()
    {
        var assignedDoctor = await SeedDoctorAsync();
        var other = await SeedDoctorAsync(specialtyId: assignedDoctor.SpecialtyId);
        var day = Today().AddDays(10);
        await InsertBlockAsync(assignedDoctor.DoctorId, day, 8, 14);
        await InsertBlockAsync(other.DoctorId, day, 8, 14);
        var patients = new[] { await SeedPatientAsync(), await SeedPatientAsync() };

        var responses = await Task.WhenAll(patients.Select(patient => patient.Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = assignedDoctor.SpecialtyId,
            startsAt = Local(day, 10),
            assignedDoctorId = assignedDoctor.DoctorId
        })));

        try
        {
            responses.ShouldAllBe(response => response.StatusCode == HttpStatusCode.Created);
            var bodies = new List<RequestAppointmentResponse>();
            foreach (var response in responses)
                bodies.Add((await response.Content.ReadFromJsonAsync<RequestAppointmentResponse>())!);
            bodies.Select(body => body.DoctorId).ShouldBe(new[] { assignedDoctor.DoctorId, other.DoctorId }, ignoreOrder: true);
            bodies.Single(body => body.DoctorId == other.DoctorId).DoctorChanged.ShouldBeTrue();
            bodies.Single(body => body.DoctorId == assignedDoctor.DoctorId).DoctorChanged.ShouldBeFalse();
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    [Fact]
    public async Task Un_paciente_sin_perfil_completo_no_puede_agendar()
    {
        var doctor = await SeedDoctorAsync();
        var email = $"sinperfil-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Patient, email, Password, mustChangePassword: false);
        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldNotBeNull();
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var response = await Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(Today().AddDays(10), 10),
            doctorId = doctor.DoctorId
        });

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_medico_no_puede_agendar_ni_pedir_la_asignacion()
    {
        var doctor = await SeedDoctorAsync();
        var login = await LoginSuccessfullyAsync(doctor.Email, Password);
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        using var request = await Client.PostAsJsonAsync(AppointmentsUrl, new
        {
            specialtyId = doctor.SpecialtyId,
            startsAt = Local(Today().AddDays(10), 10),
            doctorId = doctor.DoctorId
        });
        using var summary = await Client.GetAsync(AssignmentUrl(doctor.SpecialtyId, Local(Today().AddDays(10), 10)));

        await request.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
        await summary.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Sin_token_responde_401()
    {
        using var request = await Client.PostAsJsonAsync(AppointmentsUrl, new { specialtyId = 1, startsAt = DateTimeOffset.UtcNow });
        using var summary = await Client.GetAsync(AssignmentUrl(1, DateTimeOffset.UtcNow));

        await request.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
        await summary.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    private sealed record AppointmentRow(int PatientId, int DoctorId, int SpecialtyId, string Status, string? ReasonForVisit);

    private async Task<AppointmentRow> ReadAppointmentAsync(int appointmentId)
    {
        AppointmentRow? row = null;
        await WithContextAsync(async context => row = await context.Database.SqlQuery<AppointmentRow>(
            $"""
            SELECT patient_id, doctor_id, specialty_id, status, reason_for_visit
            FROM scheduling.appointment
            WHERE id = {appointmentId}
            """).SingleAsync());
        return row!;
    }

    private async Task<int> CountActiveAppointmentsAsync(int doctorId)
    {
        var count = 0;
        await WithContextAsync(async context => count = await context.Database.SqlQuery<int>(
            $"""
            SELECT count(*)::int AS "Value"
            FROM scheduling.appointment
            WHERE doctor_id = {doctorId} AND status IN ('pending', 'confirmed')
            """).SingleAsync());
        return count;
    }

    // Médico activo con perfil completo y una especialidad activa; sin especialidad indicada se crea una nueva.
    private async Task<DoctorSeed> SeedDoctorAsync(int? specialtyId = null)
    {
        var email = $"medico-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        DoctorSeed? seed = null;
        await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialty = specialtyId ?? await InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialty);
            var location = await context.Database.SqlQuery<LocationRow>(
                $"SELECT province_id, municipality_id FROM people.doctor WHERE id = {doctorId}").SingleAsync();
            seed = new DoctorSeed(email, doctorId, specialty, location.ProvinceId, location.MunicipalityId);
        });

        return seed!;
    }

    private sealed record LocationRow(int ProvinceId, int MunicipalityId);

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

    /// <summary>Cambia el médico a un municipio de otra provincia que la del médico de referencia.</summary>
    private Task MoveDoctorToAnotherMunicipalityAsync(DoctorSeed doctor, DoctorSeed reference) =>
        WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE people.doctor
            SET province_id = m.province_id, municipality_id = m.id
            FROM (
                SELECT id, province_id FROM people.municipality
                WHERE province_id <> {reference.ProvinceId}
                ORDER BY id
                LIMIT 1
            ) m
            WHERE people.doctor.id = {doctor.DoctorId}
            """));

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

    private readonly List<HttpClient> _clients = [];

    // El reloj falso arranca en una fecha fija; los tokens vencen según el reloj real y "hoy" se calcula con el
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

    private Task InsertBlockAsync(int doctorId, DateOnly day, int startHour, int endHour)
    {
        // Npgsql solo escribe timestamptz en UTC; la API sí acepta el desfase de Santo Domingo.
        var startsAt = Local(day, startHour).ToUniversalTime();
        var endsAt = Local(day, endHour).ToUniversalTime();

        return WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAt}, {endsAt}, true)
            """));
    }

    /// <summary>Cita pendiente de 2 horas de otro paciente nuevo, para ocupar el horario del médico.</summary>
    private async Task InsertAppointmentAsync(DoctorSeed doctor, DateOnly day, int startHour)
    {
        var email = $"otro-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Patient, email, Password, mustChangePassword: false);
        var userId = (await FindUserAsync(email))!.Id;
        // Npgsql solo escribe timestamptz en UTC; la API sí acepta el desfase de Santo Domingo.
        var startsAt = Local(day, startHour).ToUniversalTime();
        var endsAt = Local(day, startHour + AppointmentHours).ToUniversalTime();
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, userId);
            var patientId = await context.Patients.Where(patient => patient.UserId == userId)
                .Select(patient => patient.Id).SingleAsync();
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.appointment (patient_id, doctor_id, specialty_id, starts_at, ends_at, status)
                VALUES ({patientId}, {doctor.DoctorId}, {doctor.SpecialtyId}, {startsAt}, {endsAt}, 'pending')
                """);
        });
    }

    /// <summary>Verifica el status y el código del ProblemDetails, y devuelve su título ya sin escapes de JSON.</summary>
    private static async Task<string> AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        await response.ShouldHaveStatusAsync(status);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().ShouldBe(code);
        return problem.GetProperty("title").GetString()!;
    }

    private static string AssignmentUrl(
        int specialtyId, DateTimeOffset startsAt, int? provinceId = null, int? municipalityId = null)
    {
        var url = $"{AppointmentsUrl}/doctor-assignment?specialtyId={specialtyId}&startsAt={Uri.EscapeDataString(startsAt.ToString("O"))}";
        if (provinceId is not null) url += $"&provinceId={provinceId}";
        if (municipalityId is not null) url += $"&municipalityId={municipalityId}";
        return url;
    }

    // Hoy en Santo Domingo según el reloj falso de la aplicación; Santo Domingo está en UTC-4 todo el año.
    private DateOnly Today() => DateOnly.FromDateTime(Clock.UtcNow.ToOffset(TimeSpan.FromHours(-4)).DateTime);

    private static DateTimeOffset Local(DateOnly day, int hour) =>
        new(day.ToDateTime(new TimeOnly(hour, 0)), TimeSpan.FromHours(-4));

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
