using FluentValidation;

namespace SGCM.Data.Application.People.Queries.ListInsuranceProviders;

/// <summary>
/// Valida la paginación de <see cref="ListInsuranceProvidersQuery"/>.
/// </summary>
public sealed class ListInsuranceProvidersQueryValidator : AbstractValidator<ListInsuranceProvidersQuery>
{
    private const int MinimumPageNumber = 1;
    private const int MinimumPageSize = 1;
    private const int MaximumPageSize = 100;

    /// <summary>
    /// Define las reglas: página desde 1, tamaño entre 1 y 100 y un desplazamiento que quepa en un entero.
    /// </summary>
    public ListInsuranceProvidersQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(MinimumPageNumber)
            .WithErrorCode("InsuranceProvider.InvalidPageNumber")
            .WithMessage("El número de página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(MinimumPageSize, MaximumPageSize)
            .WithErrorCode("InsuranceProvider.InvalidPageSize")
            .WithMessage("El tamaño de página debe estar entre 1 y 100.");

        // Solo se evalúa con página y tamaño válidos, para que el cálculo del desplazamiento no se desborde.
        RuleFor(query => query.PageNumber)
            .Must((query, page) => ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.PageNumber >= MinimumPageNumber
                && query.PageSize is >= MinimumPageSize and <= MaximumPageSize)
            .WithErrorCode("InsuranceProvider.PageOffsetTooLarge")
            .WithMessage("El desplazamiento de la página supera el máximo permitido.");
    }
}
