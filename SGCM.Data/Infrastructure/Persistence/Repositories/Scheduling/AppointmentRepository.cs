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

    public async Task<Appointment?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("El bloqueo de Appointment requiere una transacción activa.");
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM scheduling.appointment WHERE id = {id} FOR UPDATE",
            cancellationToken);

        DetachUnchanged(id);

        // En READ COMMITTED esta sentencia nueva ya ve lo que confirmó la otra transacción mientras esperábamos.
        return await context.Appointments.AsTracking()
            .SingleOrDefaultAsync(appointment => appointment.Id == id, cancellationToken);
    }

    /// <summary>
    /// EF no reemplaza una entidad ya rastreada, así que se descarta solo esa copia para releerla tras el
    /// bloqueo; nunca se descartan cambios pendientes.
    /// </summary>
    private void DetachUnchanged(int id)
    {
        context.ChangeTracker.DetectChanges();
        var tracked = context.ChangeTracker.Entries<Appointment>().SingleOrDefault(entry => entry.Entity.Id == id);
        if (tracked is null) return;

        if (tracked.State != EntityState.Unchanged)
        {
            throw new InvalidOperationException("No se puede recargar una Appointment con cambios pendientes.");
        }

        tracked.State = EntityState.Detached;
    }
}
