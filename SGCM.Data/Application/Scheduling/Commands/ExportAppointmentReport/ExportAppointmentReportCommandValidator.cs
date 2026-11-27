using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;

/// <summary>
/// Valida los criterios con las reglas compartidas de <see cref="ReportCriteriaValidator"/> y el formato del archivo.
/// </summary>
public sealed class ExportAppointmentReportCommandValidator : AbstractValidator<ExportAppointmentReportCommand>
{
    /// <summary>Define las reglas: criterios presentes y válidos, y un formato definido.</summary>
    public ExportAppointmentReportCommandValidator()
    {
        RuleFor(command => command.Criteria)
            .NotNull()
            .WithErrorCode("Report.CriteriaRequired")
            .WithMessage("Los criterios del reporte son obligatorios.")
            .SetValidator(new ReportCriteriaValidator());

        RuleFor(command => command.Format)
            .IsInEnum()
            .WithErrorCode("Report.InvalidFormat")
            .WithMessage("El formato de exportación no es válido.");
    }
}
