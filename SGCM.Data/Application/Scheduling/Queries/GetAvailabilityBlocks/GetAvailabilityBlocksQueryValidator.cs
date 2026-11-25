using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailabilityBlocks;

/// <summary>El rango debe ir en orden y abarcar como máximo 31 días, contando ambos extremos.</summary>
public sealed class GetAvailabilityBlocksQueryValidator : AbstractValidator<GetAvailabilityBlocksQuery>
{
    public GetAvailabilityBlocksQueryValidator()
    {
        RuleFor(query => query.To)
            .GreaterThanOrEqualTo(query => query.From)
            .WithErrorCode("Availability.InvalidDateRange")
            .WithMessage("La fecha hasta no puede ser anterior a la fecha desde.");

        RuleFor(query => query.To)
            .Must((query, to) => to.DayNumber - query.From.DayNumber < GetAvailabilityBlocksQuery.MaximumDays)
            .When(query => query.To >= query.From)
            .WithErrorCode("Availability.DateRangeTooLong")
            .WithMessage($"Solo se pueden consultar hasta {GetAvailabilityBlocksQuery.MaximumDays} días a la vez.");
    }
}
