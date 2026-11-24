using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;

/// <summary>
/// Valida <see cref="GetAffectedAppointmentsQuery"/>: el bloque debe ser positivo y el horario nuevo, si se
/// envía, debe traer inicio y fin.
/// </summary>
public sealed class GetAffectedAppointmentsQueryValidator : AbstractValidator<GetAffectedAppointmentsQuery>
{
    public GetAffectedAppointmentsQueryValidator()
    {
        RuleFor(query => query.BlockId)
            .GreaterThan(0)
            .WithErrorCode("Availability.InvalidBlockId")
            .WithMessage("El identificador del bloque debe ser mayor que cero.");

        RuleFor(query => query)
            .Must(query => query.StartsAt.HasValue == query.EndsAt.HasValue)
            .WithName(nameof(GetAffectedAppointmentsQuery.StartsAt))
            .WithErrorCode("Availability.IncompleteNewSchedule")
            .WithMessage("Para reprogramar envíe el inicio y el fin nuevos; para cerrar no envíe ninguno.");
    }
}
