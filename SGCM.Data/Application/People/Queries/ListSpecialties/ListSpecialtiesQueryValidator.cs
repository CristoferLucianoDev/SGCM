using FluentValidation;

namespace SGCM.Data.Application.People.Queries.ListSpecialties;

public sealed class ListSpecialtiesQueryValidator : AbstractValidator<ListSpecialtiesQuery>
{
    public ListSpecialtiesQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode("Specialty.InvalidPageNumber")
            .WithMessage("El número de página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode("Specialty.InvalidPageSize")
            .WithMessage("El tamaño de página debe estar entre 1 y 100.");

        RuleFor(query => query.PageNumber)
            .Must((query, page) => ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.PageNumber >= 1 && query.PageSize is >= 1 and <= 100)
            .WithErrorCode("Specialty.PageOffsetTooLarge")
            .WithMessage("El desplazamiento de la página supera el máximo permitido.");
    }
}
