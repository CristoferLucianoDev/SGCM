using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Queries.ListMunicipalities;

internal sealed class ListMunicipalitiesQueryHandler(
    ILocationCatalogReader reader, IValidator<ListMunicipalitiesQuery> validator)
    : IQueryHandler<ListMunicipalitiesQuery, IReadOnlyList<MunicipalityDto>>
{
    private static readonly Error ProvinceNotFound =
        Error.NotFound("Location.ProvinceNotFound", "No se encontró la provincia solicitada.");

    public async Task<Result<IReadOnlyList<MunicipalityDto>>> HandleAsync(
        ListMunicipalitiesQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        var municipalities = await reader.ListMunicipalitiesAsync(query.ProvinceId, cancellationToken);
        return municipalities is null ? ProvinceNotFound : Result<IReadOnlyList<MunicipalityDto>>.Success(municipalities);
    }
}
