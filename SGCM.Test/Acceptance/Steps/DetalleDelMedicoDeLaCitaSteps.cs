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
using SGCM.Web.Contracts.People;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Pasos del perfil del médico en el detalle de la cita del paciente. Cada escenario tiene un solo médico, con su
/// propia especialidad, y cada cita lleva su propio bloque.
/// </summary>
[Binding]
public sealed class DetalleDelMedicoDeLaCitaSteps(ApiScenario scenario)
{
    private const string Url = "/api/patients/me/appointments";
    private const int AppointmentHours = 2;
    private const int AppointmentHour = 10;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    private int _patientUserId;
    private int _doctorId;
    private int _specialtyId;
    private int _appointmentId;
    private PatientAppointmentDetailResponse? _detail;

    [Given("que inicié sesión como paciente para ver el médico de mi cita")]
    public async Task DadoQueInicieSesionComoPaciente()
    {
        await AuthenticatedScenario.LoginAsync(scenario, "Patient", "medico-de-la-cita", id => _patientUserId = id);
        await SeedDoctorAsync();
    }

    [Given("que tengo una cita {string} con el médico asignado dentro de {int} días")]
    public async Task DadoQueTengoUnaCita(string status, int days) =>
        _appointmentId = await SeedAppointmentAsync(await OwnPatientIdAsync(), status, days);

    [Given("que tuve una cita {string} con el médico asignado hace {int} días")]
    public async Task DadoQueTuveUnaCita(string status, int days) =>
        _appointmentId = await SeedAppointmentAsync(await OwnPatientIdAsync(), status, -days);

    [Given("que otro paciente tiene una cita con el médico asignado dentro de {int} días")]
    public async Task DadoQueOtroPacienteTieneUnaCita(int days)
    {
        var other = UserMother.WithRole(UserRole.Patient, email: Email.Create($"medico-cita-otro-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(other);
        var otherPatientId = await WithContextAsync(async context =>
        {
            await TestDataSeeder.InsertPatientProfileAsync(context, other.Id);
            return await context.Patients.Where(patient => patient.UserId == other.Id).Select(patient => patient.Id).SingleAsync();
        });
        _appointmentId = await SeedAppointmentAsync(otherPatientId, "pending", days);
    }

    [Given("que el médico asignado fue desactivado")]
    public Task DadoQueElMedicoFueDesactivado() =>
        WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET is_active = false WHERE id = {_doctorId}"));

    [When("abro el detalle de mi cita con el médico asignado")]
    [When("abro el detalle de la cita del otro paciente con el médico asignado")]
    public async Task CuandoAbroElDetalle()
    {
        scenario.Response?.Dispose();
        _detail = null;
        scenario.Response = await scenario.Client.GetAsync($"{Url}/{_appointmentId}");
        if (scenario.Response.StatusCode == HttpStatusCode.OK)
            _detail = await scenario.Response.Content.ReadFromJsonAsync<PatientAppointmentDetailResponse>();
    }

    [Then("veo el perfil público del médico, igual al de su perfil")]
    public async Task EntoncesVeoElPerfilPublico()
    {
        using var response = await scenario.Client.GetAsync($"/api/doctors/{_doctorId}/profile");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        var profile = (await response.Content.ReadFromJsonAsync<DoctorPublicProfileResponse>()).ShouldNotBeNull();
        var shown = Detail().DoctorProfile.ShouldNotBeNull();

        shown.Specialties.ShouldBe(profile.Specialties);
        shown.ShouldBe(profile with { Specialties = shown.Specialties });
    }

    [Then("veo la especialidad de la cita")]
    public async Task EntoncesVeoLaEspecialidad()
    {
        Detail().Appointment.SpecialtyId.ShouldBe(_specialtyId);
        Detail().Appointment.SpecialtyName.ShouldBe(await SpecialtyNameAsync());
    }

    [Then("el detalle de la cita no es accesible")]
    public async Task EntoncesElDetalleNoEsAccesible()
    {
        ((int)scenario.Response!.StatusCode).ShouldBe(404);
        using var problem = await JsonDocument.ParseAsync(await scenario.Response.Content.ReadAsStreamAsync());
        problem.RootElement.GetProperty("code").GetString().ShouldBe("Appointment.NotFound");
    }

    [Then("veo el nombre {string} del médico y la especialidad de la cita, sin su perfil público")]
    public async Task EntoncesVeoElNombreSinPerfil(string doctorName)
    {
        var detail = Detail();
        detail.DoctorProfile.ShouldBeNull();
        detail.Appointment.DoctorId.ShouldBe(_doctorId);
        detail.Appointment.DoctorName.ShouldBe(doctorName);
        detail.Appointment.SpecialtyName.ShouldBe(await SpecialtyNameAsync());
    }

    private PatientAppointmentDetailResponse Detail()
    {
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        return _detail.ShouldNotBeNull("No se obtuvo el detalle de la cita en este escenario.");
    }

    private async Task SeedDoctorAsync()
    {
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"medico-cita-{Guid.NewGuid():N}@correo.com").Value);
        await scenario.Factory.SaveAsync(user);
        (_doctorId, _specialtyId) = await WithContextAsync(async context =>
        {
            var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
            var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
            return (doctorId, specialtyId);
        });
    }

    private async Task<int> SeedAppointmentAsync(int patientId, string status, int days)
    {
        var today = DateOnly.FromDateTime(scenario.Factory.Clock.UtcNow.ToOffset(SantoDomingo).DateTime);
        var startsAt = new DateTimeOffset(today.AddDays(days).ToDateTime(new TimeOnly(AppointmentHour, 0)), SantoDomingo)
            .ToUniversalTime();
        return await WithContextAsync(async context =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
                VALUES ({_doctorId}, NULL, {startsAt}, {startsAt.AddHours(AppointmentHours)}, true)
                """);
            return await TestDataSeeder.InsertAppointmentInStatusAsync(
                context, patientId, _doctorId, _specialtyId, startsAt, AppointmentHours, status);
        });
    }

    private Task<int> OwnPatientIdAsync() =>
        WithContextAsync(context => context.Patients.Where(patient => patient.UserId == _patientUserId)
            .Select(patient => patient.Id).SingleAsync());

    private Task<string> SpecialtyNameAsync() =>
        WithContextAsync(context => context.Specialties.Where(specialty => specialty.Id == _specialtyId)
            .Select(specialty => specialty.Name).SingleAsync());

    private async Task<T> WithContextAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = scenario.Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
