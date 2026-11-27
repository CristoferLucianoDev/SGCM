using System.Collections.Concurrent;
using System.Reflection;
using PdfSharp.Fonts;

namespace SGCM.Data.Infrastructure.Reporting;

/// <summary>
/// Entrega a PDFsharp la fuente Liberation Sans incrustada en el ensamblado, para cualquier familia que se pida. La
/// variante multiplataforma de PDFsharp no lee las fuentes del sistema en Linux, donde corren el pipeline y los
/// contenedores, así que el PDF trae su propia fuente y se ve igual en cualquier sistema.
/// </summary>
internal sealed class ReportFontResolver : IFontResolver
{
    /// <summary>Familia que usan los documentos PDF del reporte.</summary>
    public const string FamilyName = "Liberation Sans";

    private const string RegularFace = "LiberationSans-Regular";
    private const string BoldFace = "LiberationSans-Bold";
    private const string ResourcePrefix = "SGCM.Data.Infrastructure.Reporting.Fonts.";
    private const string ResourceExtension = ".ttf";

    private readonly ConcurrentDictionary<string, byte[]> _faces = new(StringComparer.Ordinal);

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(isBold ? BoldFace : RegularFace);

    public byte[] GetFont(string faceName) => _faces.GetOrAdd(faceName, ReadEmbeddedFace);

    private static byte[] ReadEmbeddedFace(string faceName)
    {
        var resource = ResourcePrefix + faceName + ResourceExtension;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"No se encontró la fuente incrustada '{resource}'.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
