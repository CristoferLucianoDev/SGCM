using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SGCM.Data.Application.Abstractions.Reporting;

namespace SGCM.Data.Infrastructure.Reporting;

/// <summary>Exporta el reporte a un documento de Word con el Open XML SDK.</summary>
internal sealed class WordReportExporter : IReportExporter
{
    private const string Extension = "docx";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    // Open XML mide el tamaño de letra en medios puntos y el grosor de los bordes en octavos de punto.
    private const string TitleHalfPoints = "28";
    private const uint BorderEighthPoints = 4;

    public ReportFormat Format => ReportFormat.Docx;

    public ExportedFile Export(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(BuildBody(report));
        }

        return new ExportedFile(report.FileName(Extension), ContentType, stream.ToArray());
    }

    private static Body BuildBody(ReportDocument report)
    {
        var body = new Body();
        body.Append(Paragraph(report.Title, bold: true, TitleHalfPoints));
        body.Append(Paragraph(report.Period));
        body.Append(Paragraph(report.GeneratedAtText));
        body.Append(Paragraph(ReportText.TotalsHeading, bold: true));
        body.Append(Table(
            [ReportText.StatusHeader, ReportText.CountHeader],
            report.TotalsByStatus.Select(total => (IReadOnlyList<string>)
                [total.Key, total.Value.ToString(CultureInfo.InvariantCulture)])));
        body.Append(Paragraph(string.Empty));
        body.Append(Table(report.Columns, report.Rows));
        return body;
    }

    private static Paragraph Paragraph(string text, bool bold = false, string? sizeHalfPoints = null)
    {
        var properties = new RunProperties();
        if (bold) properties.Append(new Bold());
        if (sizeHalfPoints is not null) properties.Append(new FontSize { Val = sizeHalfPoints });

        return new Paragraph(new Run(properties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Table Table(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var table = new Table(new TableProperties(Borders()));
        table.Append(Row(header, isHeader: true));
        foreach (var row in rows) table.Append(Row(row, isHeader: false));
        return table;
    }

    // La fila de encabezado se repite en cada página cuando la tabla ocupa más de una.
    private static TableRow Row(IReadOnlyList<string> values, bool isHeader)
    {
        var row = new TableRow();
        if (isHeader) row.Append(new TableRowProperties(new TableHeader()));
        foreach (var value in values) row.Append(new TableCell(Paragraph(value, isHeader)));
        return row;
    }

    private static TableBorders Borders() => new(
        new TopBorder { Val = BorderValues.Single, Size = BorderEighthPoints },
        new BottomBorder { Val = BorderValues.Single, Size = BorderEighthPoints },
        new LeftBorder { Val = BorderValues.Single, Size = BorderEighthPoints },
        new RightBorder { Val = BorderValues.Single, Size = BorderEighthPoints },
        new InsideHorizontalBorder { Val = BorderValues.Single, Size = BorderEighthPoints },
        new InsideVerticalBorder { Val = BorderValues.Single, Size = BorderEighthPoints });
}
