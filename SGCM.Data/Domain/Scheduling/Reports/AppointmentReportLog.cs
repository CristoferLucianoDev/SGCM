using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;

namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>
/// Fila del registro de reportes de citas: deja constancia de cada reporte que un médico genera o exporta, con los
/// criterios exactos que usó. Es de solo inserción: no tiene métodos que la modifiquen, y la base tampoco permite
/// actualizarla ni borrarla.
/// </summary>
public sealed class AppointmentReportLog : AggregateRoot
{
    /// <summary>Formatos de exportación aceptados, como los guarda la columna <c>export_format</c>.</summary>
    public static readonly IReadOnlyList<string> ExportFormats = ["pdf", "xlsx", "docx"];

    /// <summary>Longitud máxima del formato de exportación.</summary>
    public const int ExportFormatMaxLength = 10;

    private AppointmentReportLog()
    {
    }

    /// <summary>Médico que generó o exportó el reporte.</summary>
    public int DoctorId { get; private set; }

    /// <summary>Si el reporte se generó o se exportó.</summary>
    public ReportAction Action { get; private set; }

    /// <summary>Tipo de reporte.</summary>
    public ReportType ReportType { get; private set; }

    /// <summary>Filtros y orden aplicados, en el JSON de <see cref="ReportCriteria.ToJson"/>.</summary>
    public string Filters { get; private set; } = null!;

    /// <summary>Formato del archivo, solo en una exportación.</summary>
    public string? ExportFormat { get; private set; }

    /// <summary>Cantidad de filas que tenía el reporte.</summary>
    public int RowCount { get; private set; }

    /// <summary>Momento en que se generó o exportó, en UTC.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Registra la generación de un reporte en pantalla.</summary>
    /// <param name="doctorId">Médico dueño del reporte.</param>
    /// <param name="criteria">Criterios con que se generó.</param>
    /// <param name="rowCount">Cantidad total de filas del reporte, no solo las de la primera página.</param>
    /// <param name="generatedAt">Momento de la generación.</param>
    /// <exception cref="DomainException">Si el médico no es válido o la cantidad de filas es negativa.</exception>
    public static AppointmentReportLog ForGeneration(
        int doctorId, ReportCriteria criteria, int rowCount, DateTimeOffset generatedAt) =>
        Create(doctorId, ReportAction.Generate, criteria, exportFormat: null, rowCount, generatedAt);

    /// <summary>Registra la exportación de un reporte a un archivo.</summary>
    /// <param name="doctorId">Médico dueño del reporte.</param>
    /// <param name="criteria">Criterios con que se exportó.</param>
    /// <param name="exportFormat">Formato del archivo: pdf, xlsx o docx, sin distinguir mayúsculas.</param>
    /// <param name="rowCount">Cantidad de filas del archivo.</param>
    /// <param name="generatedAt">Momento de la exportación.</param>
    /// <exception cref="DomainException">
    /// Si el médico no es válido, la cantidad de filas es negativa o el formato no es uno de <see cref="ExportFormats"/>.
    /// </exception>
    public static AppointmentReportLog ForExport(
        int doctorId, ReportCriteria criteria, string exportFormat, int rowCount, DateTimeOffset generatedAt)
    {
        var format = exportFormat?.Trim().ToLowerInvariant();
        if (format is null || !ExportFormats.Contains(format))
            throw new DomainException($"El formato de exportación '{exportFormat}' no es válido.");

        return Create(doctorId, ReportAction.Export, criteria, format, rowCount, generatedAt);
    }

    // Los datos llegan del caso de uso, no del usuario: un valor inválido es un error de programación, no de negocio.
    private static AppointmentReportLog Create(
        int doctorId, ReportAction action, ReportCriteria criteria, string? exportFormat, int rowCount,
        DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        if (doctorId <= 0) throw new DomainException("El médico del registro de reportes no es válido.");
        if (rowCount < 0) throw new DomainException("La cantidad de filas del reporte no puede ser negativa.");

        return new AppointmentReportLog
        {
            DoctorId = doctorId,
            Action = action,
            ReportType = criteria.Type,
            Filters = criteria.ToJson(),
            ExportFormat = exportFormat,
            RowCount = rowCount,
            GeneratedAt = generatedAt.ToUniversalTime()
        };
    }
}
