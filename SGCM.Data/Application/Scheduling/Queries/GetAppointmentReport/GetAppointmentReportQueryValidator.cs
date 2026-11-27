using FluentValidation;

namespace SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;

/// <summary>
/// Valida los criterios con las reglas compartidas de <see cref="ReportCriteriaValidator"/> y la paginación.
/// </summary>
public sealed class GetAppointmentReportQueryValidator : AbstractValidator<GetAppointmentReportQuery>
{
    private const int MinimumPageSize = 1;

    /// <summary>
    /// Define las reglas: criterios presentes y válidos, página desde 1, tamaño entre 1 y 100, y un desplazamiento
    /// que quepa en un entero.
    /// </summary>
    public GetAppointmentReportQueryValidator()
    {
        RuleFor(query => query.Criteria)
            .NotNull()
            .WithErrorCode("Report.CriteriaRequired")
            .WithMessage("Los criterios del reporte son obligatorios.")
            .SetValidator(new ReportCriteriaValidator());

        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(GetAppointmentReportQuery.FirstPage)
            .WithErrorCode("Report.InvalidPageNumber")
            .WithMessage("El número de página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(MinimumPageSize, GetAppointmentReportQuery.MaximumPageSize)
            .WithErrorCode("Report.InvalidPageSize")
            .WithMessage($"El tamaño de página debe estar entre {MinimumPageSize} y {GetAppointmentReportQuery.MaximumPageSize}.");

        // Solo se evalúa con página y tamaño válidos, para que el cálculo del desplazamiento no se desborde.
        RuleFor(query => query.PageNumber)
            .Must((query, page) => ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.PageNumber >= GetAppointmentReportQuery.FirstPage
                && query.PageSize is >= MinimumPageSize and <= GetAppointmentReportQuery.MaximumPageSize)
            .WithErrorCode("Report.PageOffsetTooLarge")
            .WithMessage("El desplazamiento de la página supera el máximo permitido.");
    }
}
