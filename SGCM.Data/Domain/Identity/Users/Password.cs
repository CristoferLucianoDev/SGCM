using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Contraseña en claro que cumple la política de longitud. Solo existe en memoria
/// hasta que se calcula su hash; nunca se persiste. No recorta espacios: se respeta
/// exactamente lo que escribió el usuario.
/// </summary>
public sealed record Password
{
    /// <summary>
    /// Longitud mínima admitida, en caracteres.
    /// </summary>
    public const int MinLength = 8;

    /// <summary>
    /// Longitud máxima admitida, en caracteres. Evita que una contraseña enorme
    /// encarezca el cálculo del hash.
    /// </summary>
    public const int MaxLength = 128;

    /// <summary>
    /// Contraseña en claro.
    /// </summary>
    public string Value { get; }

    private Password(string value)
    {
        Value = value;
    }

    /// <summary>
    /// Valida la longitud de la contraseña.
    /// </summary>
    /// <param name="value">Contraseña tal como la escribió el usuario.</param>
    /// <returns>
    /// La contraseña, <see cref="UserErrors.PasswordTooShort"/> si no llega a
    /// <see cref="MinLength"/> o <see cref="UserErrors.PasswordTooLong"/> si supera
    /// <see cref="MaxLength"/>.
    /// </returns>
    public static Result<Password> Create(string? value)
    {
        if (value is null || value.Length < MinLength)
        {
            return UserErrors.PasswordTooShort;
        }

        if (value.Length > MaxLength)
        {
            return UserErrors.PasswordTooLong;
        }

        return new Password(value);
    }

    /// <summary>
    /// Representación enmascarada, para que la contraseña no se filtre en logs.
    /// </summary>
    public override string ToString() => "********";
}
