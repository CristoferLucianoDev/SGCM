using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Domain.Common;

namespace SGCM.Web.Api.Infrastructure;

/// <summary>
/// Base de los controladores de la API. Traduce los errores de negocio a respuestas
/// HTTP con ProblemDetails; es el único lugar donde el tipo de error decide el código.
/// </summary>
[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    private const string UnexpectedErrorTitle = "Ocurrió un error inesperado.";

    /// <summary>
    /// Código HTTP que corresponde a cada tipo de error.
    /// </summary>
    public static int ToStatusCode(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.TooManyRequests => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status500InternalServerError
    };

    /// <summary>
    /// Respuesta ProblemDetails con el código estable del error en la extensión "code".
    /// </summary>
    protected ObjectResult ErrorResult(Error error)
    {
        var statusCode = ToStatusCode(error.Type);
        var problem = new ProblemDetails
        {
            Status = statusCode,
            // Un fallo inesperado no expone su descripción interna al cliente.
            Title = statusCode == StatusCodes.Status500InternalServerError ? UnexpectedErrorTitle : error.Description
        };
        problem.Extensions["code"] = error.Code;

        return new ObjectResult(problem) { StatusCode = statusCode };
    }
}