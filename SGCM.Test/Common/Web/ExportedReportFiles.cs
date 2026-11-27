using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PdfSharp.Pdf.IO;

namespace SGCM.Test.Common.Web;

/// <summary>
/// Lee los archivos que devuelve la exportación de reportes para compararlos con la pantalla: la tabla de citas de un
/// Excel o de un Word, con el encabezado en la primera fila, y la cantidad de páginas de un PDF.
/// </summary>
public static class ExportedReportFiles
{
    public const string PdfContentType = "application/pdf";
    public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    // La tabla de citas empieza en la fila cuyo primer valor es el encabezado de la primera columna.
    private const string FirstColumnHeader = "Fecha";

    /// <summary>Filas de la tabla de citas de un libro de Excel, empezando por el encabezado.</summary>
    public static List<IReadOnlyList<string>> ReadExcelTable(byte[] content)
    {
        using var workbook = new XLWorkbook(new MemoryStream(content));
        var sheet = workbook.Worksheet(1);
        var header = sheet.RowsUsed().First(row => row.Cell(1).GetString() == FirstColumnHeader);
        var columns = header.LastCellUsed()!.Address.ColumnNumber;

        return sheet.RowsUsed()
            .Where(row => row.RowNumber() >= header.RowNumber())
            .Select(row => (IReadOnlyList<string>)Enumerable.Range(1, columns)
                .Select(column => row.Cell(column).GetString()).ToList())
            .ToList();
    }

    /// <summary>Filas de la tabla de citas de un documento de Word, la última del documento, con el encabezado.</summary>
    public static List<IReadOnlyList<string>> ReadWordTable(byte[] content)
    {
        using var document = WordprocessingDocument.Open(new MemoryStream(content), false);
        var table = document.MainDocumentPart!.Document!.Body!.Elements<Table>().Last();

        return table.Elements<TableRow>()
            .Select(row => (IReadOnlyList<string>)row.Elements<TableCell>().Select(cell => cell.InnerText).ToList())
            .ToList();
    }

    /// <summary>Cantidad de páginas de un PDF; falla si el archivo no es un PDF válido.</summary>
    public static int PdfPageCount(byte[] content)
    {
        using var document = PdfReader.Open(new MemoryStream(content), PdfDocumentOpenMode.Import);
        return document.PageCount;
    }
}
