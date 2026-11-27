using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>Errores de los reportes de citas.</summary>
public static class ReportErrors
{
    /// <summary>Los reportes muestran la actividad del médico autenticado, así que solo un médico los pide.</summary>
    public static readonly Error DoctorOnly = Error.Forbidden(
        "Report.DoctorOnly", "Solo un médico puede consultar reportes de sus citas.");

    /// <summary>Un reporte sin filas no se exporta: el archivo no tendría datos.</summary>
    public static readonly Error EmptyReport = Error.Validation(
        "Report.EmptyReport", "No hay citas para exportar con el tipo, el rango y los filtros elegidos.");
}
