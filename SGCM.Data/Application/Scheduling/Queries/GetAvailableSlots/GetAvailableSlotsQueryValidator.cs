using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;

/// <summary>
/// Valida el médico, la especialidad y los filtros de ubicación de <see cref="GetAvailableSlotsQuery"/>: cada
/// identificador, si se envía, debe ser mayor que cero.
/// </summary>
public sealed class GetAvailableSlotsQueryValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    /// <summary>
    /// Define las reglas de validación de la consulta.
    /// </summary>
    public GetAvailableSlotsQueryValidator()
    {
        RuleFor(query => query.DoctorId)
            .GreaterThan(0)
            .When(query => query.DoctorId.HasValue)
            .WithErrorCode("AvailableSlots.InvalidDoctorId")
            .WithMessage("El identificador del médico debe ser mayor que cero.");

        RuleFor(query => query.SpecialtyId)
            .GreaterThan(0)
            .When(query => query.SpecialtyId.HasValue)
            .WithErrorCode("AvailableSlots.InvalidSpecialtyId")
            .WithMessage("El identificador de la especialidad debe ser mayor que cero.");

        RuleFor(query => query.ProvinceId)
            .GreaterThan(0)
            .When(query => query.ProvinceId.HasValue)
            .WithErrorCode("AvailableSlots.InvalidProvinceId")
            .WithMessage("El identificador de la provincia debe ser mayor que cero.");

        RuleFor(query => query.MunicipalityId)
            .GreaterThan(0)
            .When(query => query.MunicipalityId.HasValue)
            .WithErrorCode("AvailableSlots.InvalidMunicipalityId")
            .WithMessage("El identificador del municipio debe ser mayor que cero.");
    }
}
