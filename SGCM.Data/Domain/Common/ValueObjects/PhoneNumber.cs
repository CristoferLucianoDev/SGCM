namespace SGCM.Data.Domain.Common.ValueObjects;

/// <summary>Teléfono de contacto con un máximo de 20 caracteres.</summary>
public sealed record PhoneNumber
{
    public const int MaxLength = 20;

    public string Value { get; }

    private PhoneNumber(string value)
    {
        Value = value;
    }

    public static Result<PhoneNumber> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CommonErrors.PhoneNumberRequired;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxLength)
        {
            return CommonErrors.PhoneNumberTooLong;
        }

        return new PhoneNumber(trimmed);
    }

    // Conserva literalmente el teléfono de filas históricas, incluso si está vacío o tiene espacios.
    // Las entradas de perfiles nuevos y de actualización se validan en Create.
    internal static PhoneNumber FromStoredValue(string value) => new(value);
}
