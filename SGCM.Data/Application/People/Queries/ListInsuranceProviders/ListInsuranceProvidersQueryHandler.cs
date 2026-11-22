using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.People.Queries.ListInsuranceProviders;

/// <summary>
/// Resuelve el listado de proveedores de seguros. Cualquiera, incluso sin sesión, ve los activos; el estado
/// solicitado y la columna de estado solo se respetan para un administrador autenticado.
/// </summary>
internal sealed class ListInsuranceProvidersQueryHandler(
    IInsuranceProviderListReader reader, ICurrentUser currentUser, IValidator<ListInsuranceProvidersQuery> validator)
    : IQueryHandler<ListInsuranceProvidersQuery, PagedResult<InsuranceProviderDto>>
{
    public async Task<Result<PagedResult<InsuranceProviderDto>>> HandleAsync(
        ListInsuranceProvidersQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        // Un rol sin autenticación no concede privilegios, aunque IsInRole lo devuelva.
        var isAdministrator = currentUser.IsAuthenticated && currentUser.IsInRole(UserRole.Administrator);
        var isActive = isAdministrator ? query.IsActive : true;
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        return await reader.ListAsync(search, isActive, isAdministrator,
            query.PageNumber, query.PageSize, cancellationToken);
    }
}
