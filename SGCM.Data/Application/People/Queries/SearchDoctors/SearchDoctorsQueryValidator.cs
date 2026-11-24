using FluentValidation;

namespace SGCM.Data.Application.People.Queries.SearchDoctors;

public sealed class SearchDoctorsQueryValidator : AbstractValidator<SearchDoctorsQuery>
{
    public SearchDoctorsQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode("Doctor.InvalidPageNumber")
            .WithMessage("El número de página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode("Doctor.InvalidPageSize")
            .WithMessage("El tamaño de página debe estar entre 1 y 100.");

        RuleFor(query => query.PageNumber)
            .Must((query, page) => ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.PageNumber >= 1 && query.PageSize is >= 1 and <= 100)
            .WithErrorCode("Doctor.PageOffsetTooLarge")
            .WithMessage("El desplazamiento de la página supera el máximo permitido.");

        RuleFor(query => query.SpecialtyId)
            .GreaterThan(0)
            .WithErrorCode("Doctor.InvalidSpecialtyId")
            .WithMessage("El identificador de la especialidad debe ser mayor que cero.");

        RuleFor(query => query.ProvinceId)
            .GreaterThan(0)
            .WithErrorCode("Doctor.InvalidProvinceId")
            .WithMessage("El identificador de la provincia debe ser mayor que cero.");

        RuleFor(query => query.MunicipalityId)
            .GreaterThan(0)
            .WithErrorCode("Doctor.InvalidMunicipalityId")
            .WithMessage("El identificador del municipio debe ser mayor que cero.");
    }
}
