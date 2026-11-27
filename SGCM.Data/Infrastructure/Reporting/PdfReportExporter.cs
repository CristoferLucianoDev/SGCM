using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using SGCM.Data.Application.Abstractions.Reporting;

namespace SGCM.Data.Infrastructure.Reporting;

/// <summary>Exporta el reporte a PDF con MigraDoc, en una página horizontal para que la tabla quepa a lo ancho.</summary>
internal sealed class PdfReportExporter : IReportExporter
{
    private const string Extension = "pdf";
    private const string ContentType = "application/pdf";
    private const double TitleSize = 16;
    private const double BodySize = 9;
    private const double SpaceAfterBlock = 10;
    private const double TotalsColumnWidthCm = 4;
    private const double PageMarginCm = 1.5;
    private const double BorderWidth = 0.5;

    public ReportFormat Format => ReportFormat.Pdf;

    public ExportedFile Export(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        ReportFonts.EnsureRegistered();

        var renderer = new PdfDocumentRenderer { Document = BuildDocument(report) };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, false);

        return new ExportedFile(report.FileName(Extension), ContentType, stream.ToArray());
    }

    private static Document BuildDocument(ReportDocument report)
    {
        var document = new Document();
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = ReportFontResolver.FamilyName;
        normal.Font.Size = BodySize;

        var section = document.AddSection();
        section.PageSetup = LandscapePage(document.DefaultPageSetup);

        AddHeader(section, report);
        AddTotals(section, report);
        AddRows(section, report, section.PageSetup);
        return document;
    }

    // La hoja horizontal se arma intercambiando el ancho y el alto de la página por defecto: en esta versión de
    // MigraDoc, Orientation.Landscape sobre una configuración clonada no gira la página.
    private static PageSetup LandscapePage(PageSetup defaults)
    {
        var page = defaults.Clone();
        page.PageWidth = defaults.PageHeight;
        page.PageHeight = defaults.PageWidth;
        page.LeftMargin = page.RightMargin = Unit.FromCentimeter(PageMarginCm);
        return page;
    }

    private static void AddHeader(Section section, ReportDocument report)
    {
        var title = section.AddParagraph(report.Title);
        title.Format.Font.Size = TitleSize;
        title.Format.Font.Bold = true;
        section.AddParagraph(report.Period);
        section.AddParagraph(report.GeneratedAtText).Format.SpaceAfter = SpaceAfterBlock;
    }

    private static void AddTotals(Section section, ReportDocument report)
    {
        section.AddParagraph(ReportText.TotalsHeading).Format.Font.Bold = true;
        var table = CreateTable(section, [TotalsColumnWidthCm, TotalsColumnWidthCm]);
        AddRow(table, [ReportText.StatusHeader, ReportText.CountHeader], isHeader: true);
        foreach (var (status, count) in report.TotalsByStatus)
            AddRow(table, [status, count.ToString(System.Globalization.CultureInfo.InvariantCulture)], isHeader: false);

        section.AddParagraph().Format.SpaceAfter = SpaceAfterBlock;
    }

    // Las columnas se reparten el ancho útil de la página en partes iguales. La fila de encabezado se repite en cada
    // página porque AddRow la marca con HeadingFormat.
    private static void AddRows(Section section, ReportDocument report, PageSetup page)
    {
        var usableWidthCm = (page.PageWidth - page.LeftMargin - page.RightMargin).Centimeter;
        var widths = Enumerable.Repeat(usableWidthCm / report.Columns.Count, report.Columns.Count).ToArray();
        var table = CreateTable(section, widths);
        AddRow(table, report.Columns, isHeader: true);
        foreach (var row in report.Rows) AddRow(table, row, isHeader: false);
    }

    private static Table CreateTable(Section section, IReadOnlyList<double> widthsCm)
    {
        var table = section.AddTable();
        table.Borders.Width = BorderWidth;
        foreach (var width in widthsCm) table.AddColumn(Unit.FromCentimeter(width));
        return table;
    }

    private static void AddRow(Table table, IReadOnlyList<string> values, bool isHeader)
    {
        var row = table.AddRow();
        row.HeadingFormat = isHeader;
        row.Format.Font.Bold = isHeader;
        for (var index = 0; index < values.Count; index++) row.Cells[index].AddParagraph(values[index]);
    }
}
