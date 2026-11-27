using ClosedXML.Excel;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Infrastructure.Reporting;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class ExcelReportExporterTests
{
    private const int RowCount = 3;
    private const int TotalsHeadingRow = 5;
    private const int TableHeaderRow = 13;

    private readonly ExcelReportExporter _exporter = new();

    [Fact]
    public void Genera_un_libro_de_excel_con_su_nombre_y_tipo()
    {
        var file = _exporter.Export(ReportDocumentMother.WithRows(RowCount));

        _exporter.Format.ShouldBe(ReportFormat.Xlsx);
        file.FileName.ShouldBe("reporte-citas-20270112-0930.xlsx");
        file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public void Escribe_el_titulo_el_rango_y_la_hora_de_generacion()
    {
        var sheet = Open(_exporter.Export(ReportDocumentMother.WithRows(RowCount)));

        sheet.Cell(1, 1).GetString().ShouldBe(ReportDocumentMother.Title);
        sheet.Cell(1, 1).Style.Font.Bold.ShouldBeTrue();
        sheet.Cell(2, 1).GetString().ShouldBe("Del 01/01/2027 al 31/01/2027");
        sheet.Cell(3, 1).GetString().ShouldBe("Generado el 12/01/2027 09:30");
    }

    [Fact]
    public void Escribe_los_totales_en_el_orden_recibido()
    {
        var sheet = Open(_exporter.Export(ReportDocumentMother.WithRows(RowCount)));

        sheet.Cell(TotalsHeadingRow, 1).GetString().ShouldBe("Totales por estado");
        ReadRow(sheet, TotalsHeadingRow + 1, 2).ShouldBe(new[] { "Estado", "Cantidad" });
        Enumerable.Range(TotalsHeadingRow + 2, 5).Select(row => sheet.Cell(row, 1).GetString())
            .ShouldBe(new[] { "Pendiente", "Confirmada", "Rechazada", "Cancelada", "Completada" });
        sheet.Cell(TotalsHeadingRow + 2, 2).GetValue<int>().ShouldBe(RowCount);
    }

    [Fact]
    public void Escribe_la_tabla_con_las_mismas_columnas_y_filas_en_el_mismo_orden()
    {
        var sheet = Open(_exporter.Export(ReportDocumentMother.WithRows(RowCount)));

        ReadRow(sheet, TableHeaderRow, ReportDocumentMother.Columns.Count).ShouldBe(ReportDocumentMother.Columns);
        sheet.Cell(TableHeaderRow, 1).Style.Font.Bold.ShouldBeTrue();
        for (var number = 1; number <= RowCount; number++)
        {
            ReadRow(sheet, TableHeaderRow + number, ReportDocumentMother.Columns.Count)
                .ShouldBe(ReportDocumentMother.Row(number));
        }

        sheet.LastRowUsed()!.RowNumber().ShouldBe(TableHeaderRow + RowCount);
    }

    private static IXLWorksheet Open(ExportedFile file) =>
        new XLWorkbook(new MemoryStream(file.Content)).Worksheet(1);

    private static List<string> ReadRow(IXLWorksheet sheet, int row, int columns) =>
        Enumerable.Range(1, columns).Select(column => sheet.Cell(row, column).GetString()).ToList();
}
