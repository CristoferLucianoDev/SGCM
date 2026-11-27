using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;

/// <summary>
/// Valida los criterios con las reglas compartidas de <see cref="ReportCriteriaValidator"/> y el tamaño de la página.
/// </summary>
public sealed class GenerateAppointmentReportCommandValidator : AbstractValidator<GenerateAppointmentReportCommand>
{
    private const int MinimumPageSize = 1;

    /// <summary>Define las reglas: criterios presentes y válidos, y página entre 1 y 100 filas.</summary>
    public GenerateAppointmentReportCommandValidator()
    {
        RuleFor(command => command.Criteria)
            .NotNull()
            .WithErrorCode("Report.CriteriaRequired")
            .WithMessage("Los criterios del reporte son obligatorios.")
            .SetValidator(new ReportCriteriaValidator());

        RuleFor(command => command.PageSize)
            .InclusiveBetween(MinimumPageSize, GenerateAppointmentReportCommand.MaximumPageSize)
            .WithErrorCode("Report.InvalidPageSize")
            .WithMessage($"El tamaño de página debe estar entre {MinimumPageSize} y {GenerateAppointmentReportCommand.MaximumPageSize}.");
    }
}
