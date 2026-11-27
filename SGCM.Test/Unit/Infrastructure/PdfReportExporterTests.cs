using PdfSharp.Pdf.IO;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Infrastructure.Reporting;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class PdfReportExporterTests
{
    private const string PdfSignature = "%PDF-";
    private const int RowsForTwoPages = 60;

    private readonly PdfReportExporter _exporter = new();

    [Fact]
    public void Genera_un_pdf_con_su_nombre_y_tipo()
    {
        var file = _exporter.Export(ReportDocumentMother.WithRows(3));

        _exporter.Format.ShouldBe(ReportFormat.Pdf);
        file.FileName.ShouldBe("reporte-citas-20270112-0930.pdf");
        file.ContentType.ShouldBe("application/pdf");
        System.Text.Encoding.ASCII.GetString(file.Content, 0, PdfSignature.Length).ShouldBe(PdfSignature);
    }

    [Fact]
    public void El_pdf_se_puede_abrir_y_usa_paginas_horizontales()
    {
        using var document = Open(_exporter.Export(ReportDocumentMother.WithRows(3)));

        document.PageCount.ShouldBe(1);
        document.Pages[0].Width.Point.ShouldBeGreaterThan(document.Pages[0].Height.Point);
    }

    [Fact]
    public void Una_tabla_larga_continua_en_otra_pagina()
    {
        using var document = Open(_exporter.Export(ReportDocumentMother.WithRows(RowsForTwoPages)));

        document.PageCount.ShouldBeGreaterThan(1);
    }

    [Fact]
    public void Un_reporte_sin_filas_tambien_se_genera()
    {
        using var document = Open(_exporter.Export(ReportDocumentMother.WithRows(0)));

        document.PageCount.ShouldBe(1);
    }

    [Fact]
    public void Se_puede_exportar_desde_varios_hilos_a_la_vez()
    {
        var files = Enumerable.Range(0, 4).AsParallel()
            .Select(_ => _exporter.Export(ReportDocumentMother.WithRows(3)))
            .ToList();

        files.ShouldAllBe(file => file.Content.Length > 0);
    }

    private static PdfSharp.Pdf.PdfDocument Open(ExportedFile file) =>
        PdfReader.Open(new MemoryStream(file.Content), PdfDocumentOpenMode.Import);
}
