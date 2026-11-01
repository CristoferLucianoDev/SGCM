using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.People.Queries.ListSpecialties;

internal sealed class ListSpecialtiesQueryHandler(
    ISpecialtyListReader reader, ICurrentUser currentUser, IValidator<ListSpecialtiesQuery> validator)
    : IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>
{
    public async Task<Result<PagedResult<SpecialtyDto>>> HandleAsync(
        ListSpecialtiesQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        var isAdministrator = currentUser.IsAuthenticated && currentUser.IsInRole(UserRole.Administrator);
        var isActive = isAdministrator ? query.IsActive : true;
        var search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();

        return await reader.ListAsync(search, isActive, isAdministrator,
            query.PageNumber, query.PageSize, cancellationToken);
    }
}
