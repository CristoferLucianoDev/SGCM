using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.People;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Scheduling;

/// <summary>El detalle de una cita del paciente con el perfil público del médico asignado.</summary>
[Trait("Category", "Integration")]
public sealed class PatientAppointmentDoctorDetailTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string Url = "/api/patients/me/appointments";
    private const string DoctorFullName = "Luis Pérez";

    [Fact]
    public async Task El_detalle_de_una_cita_propia_muestra_el_perfil_publico_del_medico_y_la_especialidad_de_la_cita()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var appointmentId = await SeedAppointmentAsync(doctor, patient, Pending, At(DaysAhead));

        var detail = await GetDetailAsync(patient, appointmentId);
        using var profileResponse = await patient.Client.GetAsync($"/api/doctors/{doctor.DoctorId}/profile");
        var profile = (await profileResponse.Content.ReadFromJsonAsync<DoctorPublicProfileResponse>()).ShouldNotBeNull();

        detail.DoctorProfile.ShouldNotBeNull();
        detail.DoctorProfile.Specialties.ShouldBe(profile.Specialties);
        detail.DoctorProfile.ShouldBe(profile with { Specialties = detail.DoctorProfile.Specialties });
        detail.Appointment.SpecialtyId.ShouldBe(doctor.SpecialtyId);
        detail.Appointment.SpecialtyName.ShouldBe(await SpecialtyNameAsync(doctor.SpecialtyId));
    }

    [Fact]
    public async Task El_detalle_de_una_cita_de_otro_paciente_no_es_accesible()
    {
        var doctor = await SeedDoctorAsync();
        var owner = await SeedPatientAsync();
        var other = await SeedPatientAsync();
        var appointmentId = await SeedAppointmentAsync(doctor, owner, Pending, At(DaysAhead));

        using var response = await other.Client.GetAsync($"{Url}/{appointmentId}");

        await AssertErrorAsync(response, HttpStatusCode.NotFound, "Appointment.NotFound");
    }

    [Fact]
    public async Task En_una_cita_pasada_con_un_medico_desactivado_muestra_su_nombre_y_la_especialidad_sin_error()
    {
        var doctor = await SeedDoctorAsync();
        var patient = await SeedPatientAsync();
        var appointmentId = await SeedAppointmentAsync(doctor, patient, Completed, At(-5));
        await WithContextAsync(context => context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET is_active = false WHERE id = {doctor.DoctorId}"));

        var detail = await GetDetailAsync(patient, appointmentId);

        detail.DoctorProfile.ShouldBeNull();
        detail.Appointment.DoctorId.ShouldBe(doctor.DoctorId);
        detail.Appointment.DoctorName.ShouldBe(DoctorFullName);
        detail.Appointment.SpecialtyName.ShouldBe(await SpecialtyNameAsync(doctor.SpecialtyId));
    }

    private async Task<PatientAppointmentDetailResponse> GetDetailAsync(PatientSeed patient, int appointmentId)
    {
        using var response = await patient.Client.GetAsync($"{Url}/{appointmentId}");
        await response.ShouldHaveStatusAsync(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PatientAppointmentDetailResponse>()).ShouldNotBeNull();
    }

    // Cada cita lleva su propio bloque, que la cubre exactamente.
    private async Task<int> SeedAppointmentAsync(DoctorSeed doctor, PatientSeed patient, string status, DateTimeOffset startsAtUtc)
    {
        await InsertBlockAsync(doctor.DoctorId, startsAtUtc, startsAtUtc.AddHours(AppointmentHours));
        var appointmentId = 0;
        await WithContextAsync(async context => appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patient.PatientId, doctor.DoctorId, doctor.SpecialtyId, startsAtUtc, AppointmentHours, status));
        return appointmentId;
    }

    private DateTimeOffset At(int dayOffset) => Local(Today().AddDays(dayOffset), AppointmentStartHour).ToUniversalTime();

    private async Task<string> SpecialtyNameAsync(int specialtyId)
    {
        var name = string.Empty;
        await WithContextAsync(async context => name = await context.Specialties
            .Where(specialty => specialty.Id == specialtyId).Select(specialty => specialty.Name).SingleAsync());
        return name;
    }
}
