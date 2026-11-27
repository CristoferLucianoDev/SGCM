using ClosedXML.Excel;
using SGCM.Data.Application.Abstractions.Reporting;

namespace SGCM.Data.Infrastructure.Reporting;

/// <summary>Exporta el reporte a un libro de Excel con ClosedXML, en una sola hoja.</summary>
internal sealed class ExcelReportExporter : IReportExporter
{
    private const string Extension = "xlsx";
    private const string ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private const string SheetName = "Reporte";
    private const int FirstColumn = 1;
    private const int TitleRow = 1;
    private const int TotalsHeadingRow = 5;
    // Una fila en blanco separa los totales de la tabla, como en el PDF y en Word.
    private const int RowsBetweenBlocks = 2;
    private const double TitleSize = 14;
    private const double ColumnWidth = 22;

    public ReportFormat Format => ReportFormat.Xlsx;

    public ExportedFile Export(ReportDocument report)
    {
        ArgumentNullException.ThrowIfNull(report);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(SheetName);

        WriteHeader(sheet, report);
        var nextRow = WriteTotals(sheet, report);
        WriteRows(sheet, report, nextRow + RowsBetweenBlocks);
        sheet.Columns(FirstColumn, Math.Max(report.Columns.Count, 2)).Width = ColumnWidth;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return new ExportedFile(report.FileName(Extension), ContentType, stream.ToArray());
    }

    private static void WriteHeader(IXLWorksheet sheet, ReportDocument report)
    {
        var title = sheet.Cell(TitleRow, FirstColumn);
        title.Value = report.Title;
        title.Style.Font.Bold = true;
        title.Style.Font.FontSize = TitleSize;
        sheet.Cell(TitleRow + 1, FirstColumn).Value = report.Period;
        sheet.Cell(TitleRow + 2, FirstColumn).Value = report.GeneratedAtText;
    }

    /// <returns>La última fila escrita.</returns>
    private static int WriteTotals(IXLWorksheet sheet, ReportDocument report)
    {
        sheet.Cell(TotalsHeadingRow, FirstColumn).Value = ReportText.TotalsHeading;
        sheet.Cell(TotalsHeadingRow, FirstColumn).Style.Font.Bold = true;
        var row = TotalsHeadingRow + 1;
        WriteRow(sheet, row, [ReportText.StatusHeader, ReportText.CountHeader], isHeader: true);
        foreach (var (status, count) in report.TotalsByStatus)
        {
            row++;
            sheet.Cell(row, FirstColumn).Value = status;
            sheet.Cell(row, FirstColumn + 1).Value = count;
        }

        return row;
    }

    private static void WriteRows(IXLWorksheet sheet, ReportDocument report, int headerRow)
    {
        WriteRow(sheet, headerRow, report.Columns, isHeader: true);
        for (var index = 0; index < report.Rows.Count; index++)
            WriteRow(sheet, headerRow + 1 + index, report.Rows[index], isHeader: false);
    }

    private static void WriteRow(IXLWorksheet sheet, int row, IReadOnlyList<string> values, bool isHeader)
    {
        for (var index = 0; index < values.Count; index++)
        {
            var cell = sheet.Cell(row, FirstColumn + index);
            cell.Value = values[index];
            cell.Style.Font.Bold = isHeader;
        }
    }
}
