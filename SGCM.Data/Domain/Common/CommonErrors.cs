namespace SGCM.Data.Domain.Common;

/// <summary>
/// Errores de los objetos de valor compartidos por todo el dominio.
/// </summary>
public static class CommonErrors
{
    /// <summary>
    /// El correo está vacío, no tiene un formato válido o supera la longitud máxima.
    /// </summary>
    public static readonly Error InvalidEmail = Error.Validation(
        code: "Common.InvalidEmail",
        description: "El correo no tiene un formato válido o supera 256 caracteres."
    );

    public static readonly Error InvalidMoney = Error.Validation(
        "Common.InvalidMoney", "El importe debe estar entre 0 y 99999999.99 y ser representable con hasta dos decimales.");

    public static readonly Error PhoneNumberRequired = Error.Validation(
        "Common.PhoneNumberRequired", "El teléfono es obligatorio.");

    public static readonly Error PhoneNumberTooLong = Error.Validation(
        "Common.PhoneNumberTooLong", $"El teléfono no puede superar {ValueObjects.PhoneNumber.MaxLength} caracteres.");

    /// <summary>
    /// Se superó el máximo de peticiones permitidas desde el mismo origen en la ventana configurada.
    /// </summary>
    public static readonly Error TooManyRequests = Error.TooManyRequests(
        code: "Common.TooManyRequests",
        description: "Demasiadas solicitudes. Intenta de nuevo más tarde."
    );
}
