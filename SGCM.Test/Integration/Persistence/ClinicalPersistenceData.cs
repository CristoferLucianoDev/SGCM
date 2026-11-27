using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Integration.Persistence;

/// <summary>Datos de prueba compartidos por las pruebas de persistencia del registro clínico.</summary>
internal static class ClinicalPersistenceData
{
    /// <summary>Inicio de las citas sembradas por omisión: en el pasado, porque el trigger T5 solo admite registro
    /// clínico desde la hora de inicio.</summary>
    public static readonly DateTimeOffset PastStartsAt = new(2026, 3, 10, 14, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Siembra un médico, un paciente y una cita entre ambos en el estado pedido, con un bloque que la cubre
    /// exactamente, como exige el trigger de coherencia de la cita.
    /// </summary>
    public static async Task<ClinicalAppointmentSeed> SeedAppointmentAsync(
        AppDbContext context, string status, DateTimeOffset? startsAtUtc = null)
    {
        var startsAt = startsAtUtc ?? PastStartsAt;
        var doctor = await DoctorPersistenceData.SeedAsync(context, associations: 1);
        var patientId = await InsertPatientAsync(context);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO scheduling.doctor_availability (doctor_id, series_id, starts_at, ends_at, is_available)
            VALUES ({doctor.DoctorId}, NULL, {startsAt}, {startsAt.AddHours(AppointmentMother.DurationHours)}, true)
            """);
        var appointmentId = await TestDataSeeder.InsertAppointmentInStatusAsync(
            context, patientId, doctor.DoctorId, doctor.SpecialtyIds[0], startsAt, AppointmentMother.DurationHours,
            status);

        return new ClinicalAppointmentSeed(appointmentId, patientId, doctor.UserId);
    }

    /// <summary>Siembra una cita confirmada en el pasado y la primera versión de su registro clínico.</summary>
    public static async Task<(ClinicalAppointmentSeed Seed, MedicalRecord Record)> SeedMedicalRecordAsync(
        AppDbContext context)
    {
        var seed = await SeedAppointmentAsync(context, "confirmed");
        var record = MedicalRecord.Create(
            seed.AppointmentId, seed.PatientId, MedicalRecordContent.Create("Faringitis", null, null).Value,
            seed.DoctorUserId, PastStartsAt.AddMinutes(30));
        context.Add(record);
        await context.SaveChangesAsync();

        return (seed, record);
    }

    /// <summary>Siembra una cuenta de paciente con su perfil.</summary>
    /// <returns>El id del perfil de paciente.</returns>
    public static async Task<int> InsertPatientAsync(AppDbContext context)
    {
        var user = UserMother.WithRole(
            UserRole.Patient, email: Email.Create($"registro-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        await TestDataSeeder.InsertPatientProfileAsync(context, user.Id);

        return await context.Patients.Where(patient => patient.UserId == user.Id)
            .Select(patient => patient.Id).SingleAsync();
    }
}

/// <summary>Cita sembrada para las pruebas del registro clínico.</summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="PatientId">Id del perfil de paciente.</param>
/// <param name="DoctorUserId">Id de la cuenta del médico, que es quien registra.</param>
internal sealed record ClinicalAppointmentSeed(int AppointmentId, int PatientId, int DoctorUserId);
