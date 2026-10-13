using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SGCM.Web.Hosting;

/// <summary>
/// Marca en Swagger con el esquema <see cref="SecuritySchemeName"/> solo las operaciones
/// que exigen autenticación.
/// </summary>
internal sealed class AuthorizeOperationFilter : IOperationFilter
{
    /// <summary>Nombre del esquema de seguridad JWT definido en Swagger.</summary>
    public const string SecuritySchemeName = "Bearer";

    // El botón Authorize de Swagger envía el token solo a los endpoints que lo requieren,
    // así los anónimos (registro, confirmación, reenvío) no aparecen con candado.
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (!RequiresAuthorization(context))
        {
            return;
        }

        operation.Security ??= [];
        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(SecuritySchemeName, context.Document)] = [],
        });
    }

    // EndpointMetadata reúne los atributos de la acción y del controlador.
    private static bool RequiresAuthorization(OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        return metadata.OfType<IAuthorizeData>().Any() && !metadata.OfType<IAllowAnonymous>().Any();
    }
}
