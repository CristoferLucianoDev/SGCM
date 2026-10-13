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

    /// <summary>
    /// Se superó el máximo de peticiones permitidas desde el mismo origen en la ventana configurada.
    /// </summary>
    public static readonly Error TooManyRequests = Error.TooManyRequests(
        code: "Common.TooManyRequests",
        description: "Demasiadas solicitudes. Intenta de nuevo más tarde."
    );
}
