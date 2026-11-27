using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorCalendar;

/// <summary>El rango debe ir en orden y abarcar como máximo 31 días, contando ambos extremos.</summary>
public sealed class GetDoctorCalendarQueryValidator : AbstractValidator<GetDoctorCalendarQuery>
{
    public GetDoctorCalendarQueryValidator()
    {
        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .WithErrorCode("Appointment.InvalidDateRange")
            .WithMessage("La fecha inicial no puede ser posterior a la fecha final.");

        RuleFor(query => query.To)
            .Must((query, to) => to.DayNumber - query.From.DayNumber < GetDoctorCalendarQuery.MaximumDays)
            .When(query => query.To >= query.From)
            .WithErrorCode("Appointment.DateRangeTooLong")
            .WithMessage($"Solo se pueden consultar hasta {GetDoctorCalendarQuery.MaximumDays} días a la vez.");
    }
}
