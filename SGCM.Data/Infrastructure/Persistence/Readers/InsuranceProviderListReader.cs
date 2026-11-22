using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IInsuranceProviderListReader"/> con una consulta de solo lectura sobre
/// <c>people.insurance_provider</c>.
/// </summary>
internal sealed class InsuranceProviderListReader(AppDbContext context) : IInsuranceProviderListReader
{
    public async Task<PagedResult<InsuranceProviderDto>> ListAsync(
        string? search, bool? isActive, bool includeStatus, int pageNumber, int pageSize,
        CancellationToken cancellationToken)
    {
        var filtered = ApplyFilters(context.InsuranceProviders.AsNoTracking(), search, isActive);
        var totalCount = await filtered.CountAsync(cancellationToken);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var page = filtered.OrderBy(provider => provider.Name).ThenBy(provider => provider.Id)
            .Skip(offset).Take(pageSize);
        var rows = await ProjectRows(page, includeStatus).ToListAsync(cancellationToken);
        var items = rows.Select(ToDto).ToList();

        return new PagedResult<InsuranceProviderDto>(items, pageNumber, pageSize, totalCount);
    }

    // Se compone sobre la misma consulta para que el conteo y la página compartan el WHERE.
    private static IQueryable<InsuranceProvider> ApplyFilters(
        IQueryable<InsuranceProvider> query, string? search, bool? isActive)
    {
        if (isActive.HasValue)
        {
            query = query.Where(provider => provider.IsActive == isActive.Value);
        }

        if (search is null) return query;

        // Contains se traduce a strpos, así que % y _ del texto buscado se comparan como texto literal.
        return query.Where(provider => PostgresFunctions.NormalizeText(provider.Name)!
            .Contains(PostgresFunctions.NormalizeText(search)!));
    }

    // El correo se proyecta como Email (EF aplica el conversor de la columna) y se mapea a texto al
    // materializar, porque EF no traduce el acceso a Email.Value dentro de la proyección SQL.
    private static IQueryable<ProviderRow> ProjectRows(IQueryable<InsuranceProvider> page, bool includeStatus) =>
        page.Select(provider => new ProviderRow(
            provider.Id,
            provider.Name,
            provider.ContactPhone,
            provider.ContactEmail,
            provider.Address,
            provider.Website,
            includeStatus ? provider.IsActive : (bool?)null));

    private static InsuranceProviderDto ToDto(ProviderRow row) =>
        new(row.Id, row.Name, row.ContactPhone, row.ContactEmail.Value, row.Address, row.Website, row.IsActive);

    private sealed record ProviderRow(
        int Id, string Name, string ContactPhone, Email ContactEmail, string Address, string? Website, bool? IsActive);
}
