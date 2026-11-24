using System.Globalization;
using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;

/// <summary>
/// Valida la paginación y los filtros de <see cref="GetPatientAppointmentHistoryQuery"/>.
/// </summary>
public sealed class GetPatientAppointmentHistoryQueryValidator : AbstractValidator<GetPatientAppointmentHistoryQuery>
{
    private const int MinimumPageNumber = 1;
    private const int MinimumPageSize = 1;
    private const int MaximumPageSize = 100;
    private const string DateOutOfRangeCode = "Appointment.DateOutOfRange";
    private const string DateFormat = "dd/MM/yyyy";

    // Rango de fechas aceptado por el filtro; fuera de él la consulta no tiene sentido para la agenda.
    private static readonly DateOnly MinimumFilterDate = new(2000, 1, 1);
    private static readonly DateOnly MaximumFilterDate = new(2100, 12, 31);

    private static readonly string DateOutOfRangeMessage =
        $"Las fechas del filtro deben estar entre {MinimumFilterDate.ToString(DateFormat, CultureInfo.InvariantCulture)} " +
        $"y {MaximumFilterDate.ToString(DateFormat, CultureInfo.InvariantCulture)}.";

    /// <summary>
    /// Define las reglas: página desde 1, tamaño entre 1 y 100, un desplazamiento que quepa en un entero, un
    /// estado definido y, con las dos fechas, la inicial no posterior a la final.
    /// </summary>
    public GetPatientAppointmentHistoryQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(MinimumPageNumber)
            .WithErrorCode("Appointment.InvalidPageNumber")
            .WithMessage("El número de página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(MinimumPageSize, MaximumPageSize)
            .WithErrorCode("Appointment.InvalidPageSize")
            .WithMessage("El tamaño de página debe estar entre 1 y 100.");

        // Solo se evalúa con página y tamaño válidos, para que el cálculo del desplazamiento no se desborde.
        RuleFor(query => query.PageNumber)
            .Must((query, page) => ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.PageNumber >= MinimumPageNumber
                && query.PageSize is >= MinimumPageSize and <= MaximumPageSize)
            .WithErrorCode("Appointment.PageOffsetTooLarge")
            .WithMessage("El desplazamiento de la página supera el máximo permitido.");

        RuleFor(query => query.Status)
            .Must(status => Enum.IsDefined(status!.Value))
            .When(query => query.Status.HasValue)
            .WithErrorCode("Appointment.InvalidStatus")
            .WithMessage("El estado indicado no es válido.");

        RuleFor(query => query.From)
            .Must((query, from) => from <= query.To)
            .When(query => query.From.HasValue && query.To.HasValue)
            .WithErrorCode("Appointment.InvalidDateRange")
            .WithMessage("La fecha inicial no puede ser posterior a la fecha final.");

        RuleFor(query => query.From)
            .Must(IsWithinFilterRange)
            .When(query => query.From.HasValue)
            .WithErrorCode(DateOutOfRangeCode)
            .WithMessage(DateOutOfRangeMessage);

        RuleFor(query => query.To)
            .Must(IsWithinFilterRange)
            .When(query => query.To.HasValue)
            .WithErrorCode(DateOutOfRangeCode)
            .WithMessage(DateOutOfRangeMessage);
    }

    private static bool IsWithinFilterRange(DateOnly? date) =>
        date is null || (date >= MinimumFilterDate && date <= MaximumFilterDate);
}
