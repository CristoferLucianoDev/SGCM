namespace SGCM.Data.Infrastructure.Persistence;

internal static class PostgresFunctions
{
    // Solo se usa dentro de consultas LINQ traducidas a la función PostgreSQL existente.
    public static string? NormalizeText(string? value) =>
        throw new NotSupportedException("La normalización de texto debe ejecutarse en PostgreSQL.");
}
