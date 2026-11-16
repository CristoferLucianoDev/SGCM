using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class DoctorProfileCatalogReader(AppDbContext context) : IDoctorProfileCatalogReader
{
    public Task<DoctorProfileLocation> GetLocationAsync(
        int provinceId, int municipalityId, CancellationToken cancellationToken) =>
        context.Database.SqlQuery<DoctorProfileLocation>($"""
            SELECT EXISTS (
                SELECT 1 FROM people.province p WHERE p.id = {provinceId} FOR KEY SHARE
            ) AS province_exists,
            (
                SELECT m.province_id FROM people.municipality m WHERE m.id = {municipalityId} FOR KEY SHARE
            ) AS municipality_province_id
            """).SingleAsync(cancellationToken);

    public async Task<IReadOnlyList<DoctorProfileSpecialty>> GetSpecialtiesAsync(
        int[] specialtyIds, CancellationToken cancellationToken) =>
        await context.Database.SqlQuery<DoctorProfileSpecialty>($"""
            SELECT s.id, s.is_active
            FROM people.specialty s
            WHERE s.id = ANY({specialtyIds})
            ORDER BY s.id
            FOR SHARE
            """).ToListAsync(cancellationToken);
}
