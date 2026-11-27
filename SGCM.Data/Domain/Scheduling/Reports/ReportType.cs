namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>
/// Tipo de reporte de citas que elige el médico. Se guarda en <c>appointment_report_log.report_type</c>.
/// </summary>
public enum ReportType
{
    /// <summary>Citas realizadas.</summary>
    Completed,

    /// <summary>Citas canceladas.</summary>
    Cancelled,

    /// <summary>Citas pendientes.</summary>
    Pending,

    /// <summary>Todas las citas, sin importar su estado.</summary>
    All
}
