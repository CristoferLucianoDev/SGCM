namespace SGCM.Data.Application.Abstractions.Reporting;

/// <summary>Formato de archivo al que se exporta un reporte.</summary>
public enum ReportFormat
{
    /// <summary>Documento PDF.</summary>
    Pdf,

    /// <summary>Libro de Excel (.xlsx).</summary>
    Xlsx,

    /// <summary>Documento de Word (.docx).</summary>
    Docx
}
