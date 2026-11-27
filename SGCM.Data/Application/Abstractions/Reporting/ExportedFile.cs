namespace SGCM.Data.Application.Abstractions.Reporting;

/// <summary>Archivo generado por un <see cref="IReportExporter"/>, listo para devolverlo al cliente.</summary>
/// <param name="FileName">Nombre del archivo con su extensión.</param>
/// <param name="ContentType">Tipo MIME del archivo.</param>
/// <param name="Content">Bytes del archivo.</param>
public sealed record ExportedFile(string FileName, string ContentType, byte[] Content);
