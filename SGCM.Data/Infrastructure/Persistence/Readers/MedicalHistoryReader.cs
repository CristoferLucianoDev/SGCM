using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IMedicalHistoryReader"/> con dos consultas de solo lectura: las citas del historial,
/// unidas a médico y especialidad, y las versiones de su registro clínico.
/// </summary>
internal sealed class MedicalHistoryReader(AppDbContext context) : IMedicalHistoryReader
{
    public async Task<IReadOnlyList<MedicalHistoryAppointmentData>> ListAsync(
        int patientId, CancellationToken cancellationToken)
    {
        var records = context.Set<MedicalRecord>().AsNoTracking();
        var appointments = await (
                from appointment in context.Appointments.AsNoTracking()
                join doctor in context.Doctors.AsNoTracking() on appointment.DoctorId equals doctor.Id
                join specialty in context.Specialties.AsNoTracking() on appointment.SpecialtyId equals specialty.Id
                where appointment.PatientId == patientId
                      && (appointment.Status == AppointmentStatus.Completed
                          || records.Any(record => record.AppointmentId == appointment.Id))
                orderby appointment.StartsAt descending, appointment.Id descending
                select new
                {
                    appointment.Id,
                    appointment.StartsAt,
                    appointment.EndsAt,
                    appointment.Status,
                    DoctorId = doctor.Id,
                    doctor.FirstName,
                    doctor.LastName,
                    SpecialtyName = specialty.Name
                })
            .ToListAsync(cancellationToken);

        var appointmentIds = appointments.Select(appointment => appointment.Id).ToList();
        // El mismo orden que vw_medical_record_current: la vigente es la de creación más reciente y, si empatan, la de
        // mayor id.
        var versions = (await records
                .Where(record => appointmentIds.Contains(record.AppointmentId))
                .OrderByDescending(record => record.CreatedAt).ThenByDescending(record => record.Id)
                .Select(record => new
                {
                    record.AppointmentId,
                    Version = new MedicalHistoryVersionData(
                        record.Id, record.Diagnosis, record.Treatment, record.Notes, record.CreatedAt)
                })
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.AppointmentId, row => row.Version);

        return appointments.Select(appointment => new MedicalHistoryAppointmentData(
                appointment.Id,
                appointment.StartsAt,
                appointment.EndsAt,
                appointment.Status,
                appointment.DoctorId,
                $"{appointment.FirstName.Trim()} {appointment.LastName.Trim()}",
                appointment.SpecialtyName,
                versions[appointment.Id].ToList()))
            .ToList();
    }
}
