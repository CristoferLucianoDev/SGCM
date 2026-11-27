using System.Globalization;
using FluentValidation;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

/// <summary>
/// Valida la página, que el estado sea uno de los que muestra la agenda, el rango de fechas, el orden y la longitud
/// del nombre del paciente.
/// </summary>
public sealed class GetDoctorScheduleQueryValidator : AbstractValidator<GetDoctorScheduleQuery>
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

    public GetDoctorScheduleQueryValidator()
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

        // La agenda solo tiene citas activas; las demás se consultan en el historial.
        RuleFor(query => query.Status)
            .Must(status => status is AppointmentStatus.Pending or AppointmentStatus.Confirmed)
            .When(query => query.Status.HasValue)
            .WithErrorCode("Appointment.InvalidScheduleStatus")
            .WithMessage("La agenda solo incluye citas pendientes y confirmadas.");

        RuleFor(query => query.Sort)
            .IsInEnum()
            .WithErrorCode("Appointment.InvalidSort")
            .WithMessage("El orden indicado no es válido.");

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

        RuleFor(query => query.PatientName)
            .Must(name => name!.Trim().Length <= GetDoctorScheduleQuery.PatientNameMaxLength)
            .When(query => query.PatientName is not null)
            .WithErrorCode("Appointment.PatientNameTooLong")
            .WithMessage($"El nombre del paciente no puede superar {GetDoctorScheduleQuery.PatientNameMaxLength} caracteres.");
    }

    private static bool IsWithinFilterRange(DateOnly? date) =>
        date is null || (date >= MinimumFilterDate && date <= MaximumFilterDate);
}
