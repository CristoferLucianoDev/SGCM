namespace SGCM.Data.Domain.Common;

/// <summary>
/// Categoría de un <see cref="Error"/>. Permite que las capas superiores lo traduzcan
/// a una respuesta adecuada (por ejemplo, un código HTTP) sin conocer el error concreto.
/// </summary>
public enum ErrorType
{
    /// <summary>Los datos recibidos no cumplen las reglas de forma o de negocio.</summary>
    Validation,

    /// <summary>El recurso solicitado no existe.</summary>
    NotFound,

    /// <summary>La operación choca con el estado actual del recurso.</summary>
    Conflict,

    /// <summary>La operación no está permitida para quien la solicita o en el estado actual.</summary>
    Forbidden,

    /// <summary>Fallo genérico que no encaja en las demás categorías.</summary>
    Failure,

    /// <summary>Las credenciales recibidas no son válidas.</summary>
    Unauthorized,

    /// <summary>Se superó el límite de intentos permitidos.</summary>
    TooManyRequests
}

/// <summary>
/// Error de dominio esperado. Se devuelve dentro de un <see cref="Result"/> en lugar
/// de lanzarse como excepción. Dos errores son iguales si coinciden código, descripción y tipo.
/// </summary>
public sealed record Error
{
    /// <summary>
    /// Código estable y legible por máquina, con el formato <c>Entidad.Motivo</c>.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// Descripción en español pensada para mostrarse al usuario.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Categoría del error.
    /// </summary>
    public ErrorType Type { get; }

    /// <summary>
    /// Ausencia de error. Es el valor de <see cref="Result.Error"/> en un resultado exitoso.
    /// </summary>
    public static readonly Error None = new(string.Empty, string.Empty, ErrorType.Failure);

    /// <summary>
    /// Crea un error. Para los casos habituales se prefieren las fábricas por tipo.
    /// </summary>
    public Error(string code, string description, ErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    /// <summary>Crea un error de tipo <see cref="ErrorType.Validation"/>.</summary>
    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);

    /// <summary>Crea un error de tipo <see cref="ErrorType.NotFound"/>.</summary>
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);

    /// <summary>Crea un error de tipo <see cref="ErrorType.Conflict"/>.</summary>
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);

    /// <summary>Crea un error de tipo <see cref="ErrorType.Forbidden"/>.</summary>
    public static Error Forbidden(string code, string description) => new(code, description, ErrorType.Forbidden);

    /// <summary>Crea un error de tipo <see cref="ErrorType.Failure"/>.</summary>
    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    /// <summary>Crea un error de tipo <see cref="ErrorType.Unauthorized"/>.</summary>
    public static Error Unauthorized(string code, string description) => new(code, description, ErrorType.Unauthorized);

    /// <summary>Crea un error de tipo <see cref="ErrorType.TooManyRequests"/>.</summary>
    public static Error TooManyRequests(string code, string description) => new(code, description, ErrorType.TooManyRequests);
}
