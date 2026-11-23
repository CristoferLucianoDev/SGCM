using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class LocationCatalogReader(AppDbContext context) : ILocationCatalogReader
{
    public async Task<IReadOnlyList<ProvinceDto>> ListProvincesAsync(CancellationToken cancellationToken) =>
        await context.Database.SqlQuery<ProvinceDto>($"""
            SELECT id, name
            FROM people.province
            ORDER BY name, id
            """).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MunicipalityDto>?> ListMunicipalitiesAsync(
        int provinceId, CancellationToken cancellationToken)
    {
        var rows = await context.Database.SqlQuery<MunicipalityRow>($"""
            SELECT m.id, m.name
            FROM people.province p
            LEFT JOIN people.municipality m ON m.province_id = p.id
            WHERE p.id = {provinceId}
            ORDER BY m.name, m.id
            """).ToListAsync(cancellationToken);

        // Una provincia sin municipios produce una sola fila con las dos columnas nulas.
        if (rows.Count == 0) return null;
        if (rows[0].Id is null) return [];

        return rows.Select(row => new MunicipalityDto(row.Id!.Value, row.Name!)).ToArray();
    }

    private sealed record MunicipalityRow(int? Id, string? Name);
}
