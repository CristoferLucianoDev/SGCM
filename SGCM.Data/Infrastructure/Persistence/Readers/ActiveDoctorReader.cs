using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IActiveDoctorReader"/> con consultas directas a <c>people.doctor</c>.
/// </summary>
internal sealed class ActiveDoctorReader(AppDbContext context) : IActiveDoctorReader
{
    public async Task<int?> FindActiveDoctorIdAsync(int doctorId, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<int>($"""
                SELECT id AS "Value" FROM people.doctor WHERE id = {doctorId} AND is_active
                """)
            .Select(id => (int?)id)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<int?> FindActiveDoctorIdByUserIdAsync(int userId, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<int>($"""
                SELECT id AS "Value" FROM people.doctor WHERE user_id = {userId} AND is_active
                """)
            .Select(id => (int?)id)
            .FirstOrDefaultAsync(cancellationToken);
}
