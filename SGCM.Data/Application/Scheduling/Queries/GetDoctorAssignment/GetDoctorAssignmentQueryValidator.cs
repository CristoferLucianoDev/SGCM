using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;

/// <summary>
/// Valida los identificadores de <see cref="GetDoctorAssignmentQuery"/>: la especialidad es obligatoria y los
/// filtros de ubicación, si se envían, deben ser mayores que cero.
/// </summary>
public sealed class GetDoctorAssignmentQueryValidator : AbstractValidator<GetDoctorAssignmentQuery>
{
    public GetDoctorAssignmentQueryValidator()
    {
        RuleFor(query => query.SpecialtyId)
            .GreaterThan(0)
            .WithErrorCode("DoctorAssignment.InvalidSpecialtyId")
            .WithMessage("El identificador de la especialidad debe ser mayor que cero.");

        RuleFor(query => query.ProvinceId)
            .GreaterThan(0)
            .When(query => query.ProvinceId.HasValue)
            .WithErrorCode("DoctorAssignment.InvalidProvinceId")
            .WithMessage("El identificador de la provincia debe ser mayor que cero.");

        RuleFor(query => query.MunicipalityId)
            .GreaterThan(0)
            .When(query => query.MunicipalityId.HasValue)
            .WithErrorCode("DoctorAssignment.InvalidMunicipalityId")
            .WithMessage("El identificador del municipio debe ser mayor que cero.");
    }
}
