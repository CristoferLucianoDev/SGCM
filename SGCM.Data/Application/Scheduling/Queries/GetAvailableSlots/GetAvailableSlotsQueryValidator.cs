using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;

public sealed class GetAvailableSlotsQueryValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    public GetAvailableSlotsQueryValidator()
    {
        RuleFor(query => query.DoctorId)
            .GreaterThan(0)
            .When(query => query.DoctorId.HasValue)
            .WithErrorCode("AvailableSlots.InvalidDoctorId")
            .WithMessage("El identificador del médico debe ser mayor que cero.");
    }
}
