using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.People;

internal sealed class DoctorRepository(AppDbContext context) : IDoctorRepository
{
    public async Task<Doctor?> GetByUserIdForUpdateAsync(int userId, CancellationToken cancellationToken)
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("El bloqueo de Doctor requiere una transacción activa.");
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM people.doctor WHERE user_id = {userId} FOR UPDATE",
            cancellationToken);

        // EF no reemplaza una entidad ya rastreada ni elimina de su colección los vínculos
        // retirados por otra transacción. Se descarta solo este grafo, nunca cambios pendientes.
        context.ChangeTracker.DetectChanges();
        var tracked = context.ChangeTracker.Entries<Doctor>()
            .SingleOrDefault(entry => entry.Entity.UserId == userId);
        if (tracked is not null)
        {
            var links = context.ChangeTracker.Entries<DoctorSpecialty>()
                .Where(entry => entry.Property<int>("DoctorId").CurrentValue == tracked.Entity.Id)
                .ToArray();
            if (tracked.State != EntityState.Unchanged || links.Any(entry => entry.State != EntityState.Unchanged))
            {
                throw new InvalidOperationException("No se puede recargar un Doctor con cambios pendientes.");
            }

            foreach (var link in links) link.State = EntityState.Detached;
            tracked.State = EntityState.Detached;
        }

        // Una sentencia posterior al bloqueo ve los vínculos confirmados mientras esperaba.
        return await context.Doctors.AsTracking()
            .Include(doctor => doctor.Specialties)
            .SingleOrDefaultAsync(doctor => doctor.UserId == userId, cancellationToken);
    }

    public void Add(Doctor doctor) => context.Doctors.Add(doctor);
}
