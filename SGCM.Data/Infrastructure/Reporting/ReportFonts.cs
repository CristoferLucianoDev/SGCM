using PdfSharp.Fonts;

namespace SGCM.Data.Infrastructure.Reporting;

/// <summary>
/// Registra una sola vez <see cref="ReportFontResolver"/> en PDFsharp. PDFsharp guarda el resolvedor en una propiedad
/// global que no se puede cambiar después de usar la primera fuente, por eso el registro vive aquí y no en el
/// contenedor de dependencias.
/// </summary>
internal static class ReportFonts
{
    private static readonly object Gate = new();

    /// <summary>Registra el resolvedor si nadie lo registró antes; es seguro llamarlo desde varios hilos.</summary>
    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver is not null) return;

        lock (Gate)
        {
            GlobalFontSettings.FontResolver ??= new ReportFontResolver();
        }
    }
}
