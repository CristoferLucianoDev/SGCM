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

    // Los casts ::integer son necesarios: PostgreSQL no infiere el tipo de un parámetro null en "IS NULL".
    public async Task<IReadOnlyList<int>> ListActiveDoctorIdsBySpecialtyAsync(
        int specialtyId, int? provinceId, int? municipalityId, CancellationToken cancellationToken) =>
        await context.Database
            .SqlQuery<int>($"""
                SELECT DISTINCT d.id AS "Value"
                FROM people.doctor d
                JOIN people.doctor_specialty ds ON ds.doctor_id = d.id
                JOIN people.specialty s ON s.id = ds.specialty_id
                WHERE s.id = {specialtyId} AND s.is_active AND d.is_active
                  AND ({provinceId}::integer IS NULL OR d.province_id = {provinceId})
                  AND ({municipalityId}::integer IS NULL OR d.municipality_id = {municipalityId})
                ORDER BY "Value"
                """)
            .ToListAsync(cancellationToken);
}
