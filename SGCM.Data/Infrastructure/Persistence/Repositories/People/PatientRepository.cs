using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.People;

internal sealed class PatientRepository(AppDbContext context) : IPatientRepository
{
    public Task<bool> ExistsByUserIdAsync(int userId, CancellationToken cancellationToken = default) =>
        context.Patients.AnyAsync(patient => patient.UserId == userId, cancellationToken);

    public Task<Patient?> GetByUserIdAsync(int userId, CancellationToken cancellationToken) =>
        context.Patients.SingleOrDefaultAsync(patient => patient.UserId == userId, cancellationToken);

    public void Add(Patient patient) => context.Patients.Add(patient);
}
