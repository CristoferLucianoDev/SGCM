using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Infrastructure.Reporting;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class WordReportExporterTests
{
    private const int RowCount = 3;

    private readonly WordReportExporter _exporter = new();

    [Fact]
    public void Genera_un_documento_de_word_con_su_nombre_y_tipo()
    {
        var file = _exporter.Export(ReportDocumentMother.WithRows(RowCount));

        _exporter.Format.ShouldBe(ReportFormat.Docx);
        file.FileName.ShouldBe("reporte-citas-20270112-0930.docx");
        file.ContentType.ShouldBe("application/vnd.openxmlformats-officedocument.wordprocessingml.document");
    }

    [Fact]
    public void Escribe_el_titulo_el_rango_la_hora_y_el_titulo_de_los_totales()
    {
        using var document = Open(_exporter.Export(ReportDocumentMother.WithRows(RowCount)));
        var paragraphs = Body(document).Elements<Paragraph>().Select(paragraph => paragraph.InnerText).ToList();

        paragraphs.Take(4).ShouldBe(new[]
        {
            ReportDocumentMother.Title, "Del 01/01/2027 al 31/01/2027", "Generado el 12/01/2027 09:30", "Totales por estado"
        });
    }

    [Fact]
    public void Escribe_los_totales_y_la_tabla_con_las_mismas_columnas_y_filas_en_el_mismo_orden()
    {
        using var document = Open(_exporter.Export(ReportDocumentMother.WithRows(RowCount)));
        var tables = Body(document).Elements<Table>().Select(ReadTable).ToList();

        tables.Count.ShouldBe(2);
        tables[0][0].ShouldBe(new[] { "Estado", "Cantidad" });
        tables[0].Skip(1).Select(row => row[0])
            .ShouldBe(new[] { "Pendiente", "Confirmada", "Rechazada", "Cancelada", "Completada" });
        tables[1][0].ShouldBe(ReportDocumentMother.Columns);
        tables[1].Skip(1).ShouldBe(Enumerable.Range(1, RowCount).Select(ReportDocumentMother.Row));
    }

    [Fact]
    public void Un_reporte_sin_filas_deja_solo_el_encabezado_de_la_tabla()
    {
        using var document = Open(_exporter.Export(ReportDocumentMother.WithRows(0)));

        ReadTable(Body(document).Elements<Table>().Last()).ShouldHaveSingleItem().ShouldBe(ReportDocumentMother.Columns);
    }

    private static WordprocessingDocument Open(ExportedFile file) =>
        WordprocessingDocument.Open(new MemoryStream(file.Content), false);

    private static Body Body(WordprocessingDocument document) => document.MainDocumentPart!.Document!.Body!;

    private static List<IReadOnlyList<string>> ReadTable(Table table) =>
        table.Elements<TableRow>()
            .Select(row => (IReadOnlyList<string>)row.Elements<TableCell>().Select(cell => cell.InnerText).ToList())
            .ToList();
}
