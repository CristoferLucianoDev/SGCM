namespace SGCM.Data.Application.Abstractions.Reporting;

/// <summary>
/// Escribe un <see cref="ReportDocument"/> en un formato de archivo. Hay una implementación por formato y el caso de uso
/// elige la suya por <see cref="Format"/>, sin un switch por formato.
/// </summary>
public interface IReportExporter
{
    /// <summary>Formato que genera este exportador.</summary>
    ReportFormat Format { get; }

    /// <summary>Genera el archivo con el título, el rango, los totales y la tabla, con las columnas en el mismo orden.</summary>
    /// <param name="report">Contenido del reporte.</param>
    /// <returns>El archivo generado.</returns>
    ExportedFile Export(ReportDocument report);
}
