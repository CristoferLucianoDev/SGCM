using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

/// <summary>
/// Repositorio de citas. Los traslapes los resuelve la base con ex_appointment_doctor_no_overlap y
/// ex_appointment_patient_no_overlap; <see cref="UnitOfWork"/> los traduce a
/// <see cref="AppointmentErrors.SlotUnavailable"/> y <see cref="AppointmentErrors.PatientOverlap"/> al guardar.
/// </summary>
internal sealed class AppointmentRepository(AppDbContext context) : IAppointmentRepository
{
    public void Add(Appointment appointment) => context.Appointments.Add(appointment);

    public Task<Appointment?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.Appointments.AsTracking().SingleOrDefaultAsync(appointment => appointment.Id == id, cancellationToken);
}
