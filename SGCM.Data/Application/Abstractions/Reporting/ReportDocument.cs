using System.Globalization;

namespace SGCM.Data.Application.Abstractions.Reporting;

/// <summary>
/// Contenido de un reporte listo para exportar, independiente del formato: el caso de uso lo arma una sola vez y cada
/// <see cref="IReportExporter"/> lo escribe en su formato con el mismo título, rango, totales y tabla.
/// </summary>
/// <param name="Title">Título del reporte.</param>
/// <param name="From">Primer día del rango, incluido.</param>
/// <param name="To">Último día del rango, incluido.</param>
/// <param name="GeneratedAt">Momento en que se generó, en la hora local de la agenda.</param>
/// <param name="Columns">Encabezados de la tabla, en el orden en que se muestran.</param>
/// <param name="Rows">Filas de la tabla, con un valor por columna y en el mismo orden que en pantalla.</param>
/// <param name="TotalsByStatus">Cantidad de citas por estado, con el nombre del estado tal como se muestra.</param>
public sealed record ReportDocument(
    string Title,
    DateOnly From,
    DateOnly To,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> Columns,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyDictionary<string, int> TotalsByStatus)
{
    private const string DateFormat = "dd/MM/yyyy";
    private const string DateTimeFormat = "dd/MM/yyyy HH:mm";
    private const string FileTimestampFormat = "yyyyMMdd-HHmm";
    private const string FileNamePrefix = "reporte-citas-";

    /// <summary>Rango del reporte como se muestra en el archivo, por ejemplo "Del 01/01/2027 al 31/01/2027".</summary>
    public string Period =>
        $"Del {From.ToString(DateFormat, CultureInfo.InvariantCulture)} al {To.ToString(DateFormat, CultureInfo.InvariantCulture)}";

    /// <summary>Momento de generación como se muestra en el archivo, por ejemplo "Generado el 12/01/2027 09:30".</summary>
    public string GeneratedAtText => $"Generado el {GeneratedAt.ToString(DateTimeFormat, CultureInfo.InvariantCulture)}";

    /// <summary>Nombre del archivo con la extensión del formato, por ejemplo "reporte-citas-20270112-0930.pdf".</summary>
    /// <param name="extension">Extensión sin punto.</param>
    public string FileName(string extension) =>
        $"{FileNamePrefix}{GeneratedAt.ToString(FileTimestampFormat, CultureInfo.InvariantCulture)}.{extension}";
}
