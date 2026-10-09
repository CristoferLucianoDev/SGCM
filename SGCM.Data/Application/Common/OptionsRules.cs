using System.Text;

namespace SGCM.Data.Application.Common;

public static class OptionsRules
{
    public static void Required(ICollection<string> errors, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{Describe(key)} es obligatorio.");
        }
    }

    public static void Positive(ICollection<string> errors, string key, int value)
    {
        if (value <= 0)
        {
            errors.Add($"{Describe(key)} debe ser mayor que 0 (valor actual: {value}).");
        }
    }

    public static void NonNegative(ICollection<string> errors, string key, int value)
    {
        if (value < 0)
        {
            errors.Add($"{Describe(key)} no puede ser negativo (valor actual: {value}).");
        }
    }

    public static void Positive(ICollection<string> errors, string key, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            errors.Add($"{Describe(key)} debe ser un intervalo mayor que cero (valor actual: {value}).");
        }
    }

    public static void MinimumBytes(ICollection<string> errors, string key, string? value, int minimumBytes)
    {
        if (value is not null && Encoding.UTF8.GetByteCount(value) < minimumBytes)
        {
            errors.Add($"{Describe(key)} debe tener al menos {minimumBytes} bytes.");
        }
    }

    public static string Describe(string key) =>
        $"{key} (variable de entorno {key.Replace(":", "__")})";

    public static void AbsoluteHttpsUrl(ICollection<string> errors, string key, string? value)
    {
        // El valor vacío lo reporta Required.
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add($"{Describe(key)} debe ser una URL absoluta https (valor actual: {value}).");
        }
    }
}