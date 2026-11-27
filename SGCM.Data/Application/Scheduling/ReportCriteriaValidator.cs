using FluentValidation;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Valida los <see cref="ReportCriteria"/> que comparten la generación, el filtrado y la exportación de reportes.
/// </summary>
public sealed class ReportCriteriaValidator : AbstractValidator<ReportCriteria>
{
    private const string DateRangeRequiredCode = "Report.DateRangeRequired";
    private const string DateRangeRequiredMessage = "El rango de fechas es obligatorio.";

    /// <summary>
    /// Define las reglas: tipo, estado, columna y sentido del orden definidos; las dos fechas presentes y la
    /// inicial no posterior a la final; el texto del paciente dentro de su longitud y la especialidad positiva.
    /// </summary>
    public ReportCriteriaValidator()
    {
        AddEnumRules();
        AddDateRangeRules();
        AddFilterRules();
    }

    private void AddEnumRules()
    {
        RuleFor(criteria => criteria.Type)
            .IsInEnum()
            .WithErrorCode("Report.InvalidType")
            .WithMessage("El tipo de reporte no es válido.");

        RuleFor(criteria => criteria.SortBy)
            .IsInEnum()
            .WithErrorCode("Report.InvalidSortField")
            .WithMessage("La columna de orden no es válida.");

        RuleFor(criteria => criteria.SortDirection)
            .IsInEnum()
            .WithErrorCode("Report.InvalidSortDirection")
            .WithMessage("El sentido del orden no es válido.");
    }

    private void AddDateRangeRules()
    {
        RuleFor(criteria => criteria.From)
            .NotEmpty()
            .WithErrorCode(DateRangeRequiredCode)
            .WithMessage(DateRangeRequiredMessage);

        RuleFor(criteria => criteria.To)
            .NotEmpty()
            .WithErrorCode(DateRangeRequiredCode)
            .WithMessage(DateRangeRequiredMessage);

        RuleFor(criteria => criteria.From)
            .LessThanOrEqualTo(criteria => criteria.To)
            .When(criteria => criteria.From != default && criteria.To != default)
            .WithErrorCode("Report.InvalidDateRange")
            .WithMessage("La fecha inicial no puede ser posterior a la fecha final.");
    }

    private void AddFilterRules()
    {
        RuleFor(criteria => criteria.Status)
            .IsInEnum()
            .When(criteria => criteria.Status.HasValue)
            .WithErrorCode("Report.InvalidStatus")
            .WithMessage("El estado indicado no es válido.");

        RuleFor(criteria => criteria.PatientName)
            .MaximumLength(ReportCriteria.PatientNameMaxLength)
            .When(criteria => criteria.PatientName is not null)
            .WithErrorCode("Report.PatientNameTooLong")
            .WithMessage($"El nombre del paciente no puede superar los {ReportCriteria.PatientNameMaxLength} caracteres.");

        RuleFor(criteria => criteria.SpecialtyId)
            .GreaterThan(0)
            .When(criteria => criteria.SpecialtyId.HasValue)
            .WithErrorCode("Report.InvalidSpecialtyId")
            .WithMessage("La especialidad indicada no es válida.");
    }
}
