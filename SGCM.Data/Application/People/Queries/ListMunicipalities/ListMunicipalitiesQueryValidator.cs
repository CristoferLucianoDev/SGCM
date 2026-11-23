using FluentValidation;

namespace SGCM.Data.Application.People.Queries.ListMunicipalities;

public sealed class ListMunicipalitiesQueryValidator : AbstractValidator<ListMunicipalitiesQuery>
{
    public ListMunicipalitiesQueryValidator()
    {
        RuleFor(query => query.ProvinceId)
            .GreaterThan(0)
            .WithErrorCode("Location.InvalidProvinceId")
            .WithMessage("El identificador de la provincia debe ser mayor que cero.");
    }
}
