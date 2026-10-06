using System.Net.Mail;

namespace SGCM.Data.Domain.Common.ValueObjects;

/// <summary>
/// Dirección de correo electrónico válida y normalizada (sin espacios en los extremos
/// y en minúsculas). Dos correos son iguales si su valor normalizado coincide.
/// </summary>
public sealed record Email
{
    /// <summary>
    /// Longitud máxima admitida, en caracteres.
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>
    /// Correo normalizado.
    /// </summary>
    public string Value { get; }

    private Email(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Valida y normaliza un correo. Rechaza los valores vacíos, los que superan
    /// <see cref="MaxLength"/> y los que incluyen un nombre visible
    /// (por ejemplo <c>Nombre &lt;correo@dominio.com&gt;</c>).
    /// </summary>
    /// <param name="value">Correo tal como lo escribió el usuario.</param>
    /// <returns>El correo, o <see cref="CommonErrors.InvalidEmail"/> si no es válido.</returns>
    public static Result<Email> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CommonErrors.InvalidEmail;
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > MaxLength)
        {
            return CommonErrors.InvalidEmail;
        }

        if (!MailAddress.TryCreate(normalized, out var address) || address.Address != normalized)
        {
            return CommonErrors.InvalidEmail;
        }

        return new Email(normalized);
    }
}
