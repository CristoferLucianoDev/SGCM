using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

[Trait("Category", "Integration")]
public sealed class GetAvailableSlotsTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Password = "Secreta123";
    private const string OwnUrl = "/api/doctors/me/available-slots";
    private const string Pending = "pending";
    private const string Confirmed = "confirmed";
    private const string Rejected = "rejected";
    private const string Cancelled = "cancelled";
    private const string Completed = "completed";
    private const int MinimumBookingLeadDays = 4;
    private const int BookingHorizonDays = 90;
    private const int AppointmentHours = 2;
    private const int BlockStartHour = 8;
    private const int BlockEndHour = 16;

    private static readonly TimeZoneInfo SantoDomingo = TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    private sealed record DoctorSeed(string Email, int DoctorId, int SpecialtyId);

    [Fact]
    public async Task El_paciente_recibe_las_horas_libres_del_ejemplo_del_PBI_y_la_respuesta_viene_en_UTC()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, BlockStartHour, BlockEndHour);
        await InsertAppointmentAsync(doctor, day, 10, 12, Confirmed);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        var slots = await ReadSlotsAsync(response);
        slots.Select(slot => slot.StartsAt).ShouldBe(Starts(day, 8, 12, 13, 14));
        slots.ShouldAllBe(slot => slot.EndsAt - slot.StartsAt == TimeSpan.FromHours(AppointmentHours));
        slots.ShouldAllBe(slot => slot.StartsAt.Offset == TimeSpan.Zero && slot.EndsAt.Offset == TimeSpan.Zero);
    }

    [Fact]
    public async Task La_respuesta_expone_las_propiedades_items_startsAt_y_endsAt()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, BlockStartHour, BlockEndHour);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var first = body.GetProperty("items")[0];
        first.GetProperty("startsAt").GetDateTimeOffset().ShouldBe(Utc(day, 8));
        first.GetProperty("endsAt").GetDateTimeOffset().ShouldBe(Utc(day, 10));
    }

    [Fact]
    public async Task La_ventana_del_paciente_excluye_el_dia_3_incluye_el_4_incluye_el_90_y_excluye_el_91()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var today = Today();
        var beforeWindow = today.AddDays(MinimumBookingLeadDays - 1);
        var firstDay = today.AddDays(MinimumBookingLeadDays);
        var lastDay = today.AddDays(BookingHorizonDays);
        var afterWindow = today.AddDays(BookingHorizonDays + 1);
        foreach (var day in new[] { beforeWindow, firstDay, lastDay, afterWindow })
            await InsertBlockAsync(doctor.DoctorId, day, BlockStartHour, BlockEndHour);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        var starts = (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt).ToList();
        starts.ShouldBe(Starts(firstDay, 8, 9, 10, 11, 12, 13, 14).Concat(Starts(lastDay, 8, 9, 10, 11, 12, 13, 14)));
    }

    [Fact]
    public async Task La_ventana_del_medico_empieza_en_la_hora_actual_y_redondea_hacia_arriba_dentro_de_un_bloque_ya_iniciado()
    {
        var doctor = await SeedDoctorAsync();
        await AuthorizeAsync(doctor.Email);
        var today = Today();
        await InsertBlockAsync(doctor.DoctorId, today, BlockStartHour, BlockEndHour);
        // 09:30 locales: las 8 y las 9 ya pasaron, y las 9:30 no está en punto, así que la primera hora es las 10.
        Clock.UtcNow = Utc(today, new TimeOnly(9, 30));

        using var response = await Client.GetAsync(OwnUrl);

        (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt).ShouldBe(Starts(today, 10, 11, 12, 13, 14));
    }

    [Fact]
    public async Task La_ventana_del_medico_incluye_una_hora_que_coincide_exactamente_con_ahora()
    {
        var doctor = await SeedDoctorAsync();
        await AuthorizeAsync(doctor.Email);
        var today = Today();
        await InsertBlockAsync(doctor.DoctorId, today, BlockStartHour, BlockEndHour);
        Clock.UtcNow = Utc(today, 10);

        using var response = await Client.GetAsync(OwnUrl);

        (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt).ShouldBe(Starts(today, 10, 11, 12, 13, 14));
    }

    [Fact]
    public async Task El_medico_ve_un_bloque_de_manana_que_el_paciente_no_veria_por_la_antelacion_minima()
    {
        var doctor = await SeedDoctorAsync();
        await AuthorizeAsync(doctor.Email);
        var today = Today();
        await InsertBlockAsync(doctor.DoctorId, today.AddDays(1), BlockStartHour, BlockEndHour);
        Clock.UtcNow = Utc(today, 7);

        using var response = await Client.GetAsync(OwnUrl);

        (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt)
            .ShouldBe(Starts(today.AddDays(1), 8, 9, 10, 11, 12, 13, 14));
    }

    [Fact]
    public async Task Un_bloque_cerrado_no_ofrece_horas_y_uno_abierto_si()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var closedDay = Today().AddDays(10);
        var openDay = Today().AddDays(11);
        await InsertBlockAsync(doctor.DoctorId, closedDay, BlockStartHour, BlockEndHour, isAvailable: false);
        await InsertBlockAsync(doctor.DoctorId, openDay, BlockStartHour, BlockEndHour);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt)
            .ShouldBe(Starts(openDay, 8, 9, 10, 11, 12, 13, 14));
    }

    [Theory]
    [InlineData(Pending, true)]
    [InlineData(Confirmed, true)]
    [InlineData(Cancelled, false)]
    [InlineData(Rejected, false)]
    [InlineData(Completed, false)]
    public async Task Solo_las_citas_pending_y_confirmed_bloquean_el_horario(string status, bool blocks)
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, BlockStartHour, BlockEndHour);
        await InsertAppointmentAsync(doctor, day, 10, 12, status);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        var expected = blocks ? Starts(day, 8, 12, 13, 14) : Starts(day, 8, 9, 10, 11, 12, 13, 14);
        (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt).ShouldBe(expected);
    }

    [Fact]
    public async Task Los_bloques_y_las_citas_de_otro_medico_no_afectan_el_resultado()
    {
        var doctor = await SeedDoctorAsync();
        var otherDoctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, BlockStartHour, BlockEndHour);
        await InsertBlockAsync(otherDoctor.DoctorId, day, BlockStartHour, BlockEndHour);
        await InsertBlockAsync(otherDoctor.DoctorId, day.AddDays(1), BlockStartHour, BlockEndHour);
        await InsertAppointmentAsync(otherDoctor, day, 8, 10, Confirmed);
        await InsertAppointmentAsync(otherDoctor, day, 12, 14, Pending);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        (await ReadSlotsAsync(response)).Select(slot => slot.StartsAt).ShouldBe(Starts(day, 8, 9, 10, 11, 12, 13, 14));
    }

    [Fact]
    public async Task Un_medico_sin_bloques_responde_200_con_items_vacio()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        (await ReadSlotsAsync(response)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Con_el_bloque_completamente_ocupado_responde_200_con_items_vacio()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        var day = Today().AddDays(10);
        await InsertBlockAsync(doctor.DoctorId, day, BlockStartHour, BlockEndHour);
        foreach (var startHour in new[] { 8, 10, 12, 14 })
            await InsertAppointmentAsync(doctor, day, startHour, startHour + AppointmentHours, Confirmed);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        (await ReadSlotsAsync(response)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_inexistente_responde_404()
    {
        await LoginPatientAsync();

        using var response = await Client.GetAsync(ByDoctorUrl(int.MaxValue));

        await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).ShouldBe(DoctorErrors.NotFound.Code);
    }

    [Fact]
    public async Task Un_medico_inactivo_responde_404_aunque_tenga_bloques()
    {
        var doctor = await SeedDoctorAsync();
        await LoginPatientAsync();
        await InsertBlockAsync(doctor.DoctorId, Today().AddDays(10), BlockStartHour, BlockEndHour);
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET is_active = false WHERE id = {doctor.DoctorId}"));

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.NotFound);
        (await response.ReadErrorCodeAsync()).ShouldBe(DoctorErrors.NotFound.Code);
    }

    [Fact]
    public async Task Un_doctorId_en_cero_responde_400_con_el_codigo_InvalidDoctorId()
    {
        await LoginPatientAsync();

        using var response = await Client.GetAsync(ByDoctorUrl(0));

        await response.ShouldHaveStatusAsync(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe("AvailableSlots.InvalidDoctorId");
    }

    [Fact]
    public async Task Un_medico_en_la_ruta_del_paciente_recibe_403()
    {
        var doctor = await SeedDoctorAsync();
        await AuthorizeAsync(doctor.Email);

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_administrador_en_la_ruta_del_paciente_recibe_403()
    {
        var doctor = await SeedDoctorAsync();
        await LoginAdministratorAsync();

        using var response = await Client.GetAsync(ByDoctorUrl(doctor.DoctorId));

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_paciente_en_la_ruta_del_medico_recibe_403()
    {
        await LoginPatientAsync();

        using var response = await Client.GetAsync(OwnUrl);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_administrador_en_la_ruta_del_medico_recibe_403()
    {
        await LoginAdministratorAsync();

        using var response = await Client.GetAsync(OwnUrl);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Un_medico_con_el_perfil_pendiente_recibe_403_en_su_ruta()
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var email = $"pendiente-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldNotBeNull();
        Authorize(login.AccessToken);

        using var response = await Client.GetAsync(OwnUrl);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/doctors/1/available-slots")]
    [InlineData(OwnUrl)]
    public async Task Sin_token_responde_401(string url)
    {
        using var response = await Client.GetAsync(url);

        await response.ShouldHaveStatusAsync(HttpStatusCode.Unauthorized);
    }

    // Médico activo con perfil completo y una especialidad activa asociada, que las citas exigen.
    private async Task<DoctorSeed> SeedDoctorAsync()
    {
        var email = $"medico-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(UserRole.Doctor, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        var doctorId = 0;
        var specialtyId = 0;
        await WithContextAsync(async context =>
        {
            doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var name = $"Especialidad {Guid.NewGuid():N}";
            specialtyId = (await context.Database.SqlQuery<int>(
                $"""
                INSERT INTO people.specialty (name, description)
                VALUES ({name}, 'Descripción de prueba.')
                RETURNING id AS "Value"
                """).ToListAsync()).Single();
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
        });

        return new DoctorSeed(email, doctorId, specialtyId);
    }

    // Los tokens vencen según el reloj real, así que el reloj falso arranca en la hora actual al iniciar sesión.
    private async Task AuthorizeAsync(string email)
    {
        Clock.UtcNow = DateTimeOffset.UtcNow;
        var login = await LoginSuccessfullyAsync(email, Password);
        login.PendingStep.ShouldBeNull();
        Authorize(login.AccessToken);
    }

    private async Task LoginPatientAsync() => await LoginWithRoleAsync(UserRole.Patient, "paciente");

    private async Task LoginAdministratorAsync() => await LoginWithRoleAsync(UserRole.Administrator, "admin");

    private async Task LoginWithRoleAsync(UserRole role, string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@correo.com";
        await SaveUserAsync(role, email, Password, mustChangePassword: false);
        var user = (await FindUserAsync(email))!;
        await WithContextAsync(context => TestDataSeeder.InsertProfileAsync(context, user));
        await AuthorizeAsync(email);
    }

    private Task InsertBlockAsync(int doctorId, DateOnly day, int startHour, int endHour, bool isAvailable = true)
    {
        var startsAt = Utc(day, startHour);
        var endsAt = Utc(day, endHour);

        return WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctorId}, NULL, {startsAt}, {endsAt}, {isAvailable})
            """));
    }

    // Cada cita usa un paciente nuevo para no chocar con la exclusión de traslape del paciente. La base solo permite
    // crearla pendiente o confirmada, así que los estados finales se alcanzan por cambio de estado.
    private async Task InsertAppointmentAsync(DoctorSeed doctor, DateOnly day, int startHour, int endHour, string status)
    {
        var email = $"cita-{Guid.NewGuid():N}@correo.com";
        await SaveAsync(UserMother.WithRole(UserRole.Patient, email: Email.Create(email).Value));
        var userId = (await FindUserAsync(email))!.Id;
        var startsAt = Utc(day, startHour);
        var endsAt = Utc(day, endHour);
        var creationStatus = status is Completed ? Confirmed : status is Rejected or Cancelled ? Pending : status;
        await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, userId);
            var patientId = await context.Patients.Where(patient => patient.UserId == userId)
                .Select(patient => patient.Id).SingleAsync();
            var id = (await context.Database.SqlQuery<int>(
                $"""
                INSERT INTO scheduling.appointment (patient_id, doctor_id, specialty_id, starts_at, ends_at, status)
                VALUES ({patientId}, {doctor.DoctorId}, {doctor.SpecialtyId}, {startsAt}, {endsAt}, {creationStatus})
                RETURNING id AS "Value"
                """).ToListAsync()).Single();
            if (creationStatus == status) return;

            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE scheduling.appointment
                SET status = {status},
                    status_reason = CASE WHEN {status} = 'rejected' THEN 'Motivo de la prueba' ELSE status_reason END
                WHERE id = {id}
                """);
        });
    }

    private static string ByDoctorUrl(int doctorId) => $"/api/doctors/{doctorId}/available-slots";

    private static async Task<IReadOnlyList<AvailableSlotResponse>> ReadSlotsAsync(HttpResponseMessage response)
    {
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);

        return (await response.Content.ReadFromJsonAsync<AvailableSlotsResponse>())!.Items;
    }

    // Hoy en Santo Domingo según el reloj falso de la aplicación.
    private DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Clock.UtcNow, SantoDomingo).DateTime);

    private static IEnumerable<DateTimeOffset> Starts(DateOnly day, params int[] hours) =>
        hours.Select(hour => Utc(day, hour)).ToList();

    private static DateTimeOffset Utc(DateOnly date, int hour) => Utc(date, new TimeOnly(hour, 0));

    // Santo Domingo está en UTC-4 todo el año.
    private static DateTimeOffset Utc(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), TimeSpan.FromHours(-4)).ToUniversalTime();

    private void Authorize(string? accessToken) =>
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

    private async Task WithContextAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
