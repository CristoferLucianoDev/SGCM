using Microsoft.OpenApi;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Contracts.People;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SGCM.Web.Hosting;

internal sealed class DoctorProfileOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(DoctorProfilesController)
            || context.MethodInfo.Name != nameof(DoctorProfilesController.GetProfile)) return;

        operation.Summary = "Consulta el perfil de un médico.";
        operation.Description = "El acceso es anónimo. Solo un administrador con cuenta y sesión vigentes "
            + "recibe el perfil público junto con el teléfono y la licencia. Un identificador entero no positivo "
            + "devuelve 400. Un segmento fuera de Int32 devuelve 404 de enrutamiento.";
        operation.Security =
        [
            new(),
            new() { [new OpenApiSecuritySchemeReference(AuthorizeOperationFilter.SecuritySchemeName, context.Document)] = [] }
        ];

        var publicSchema = context.SchemaGenerator.GenerateSchema(typeof(DoctorPublicProfileResponse), context.SchemaRepository);
        var adminSchema = context.SchemaGenerator.GenerateSchema(typeof(DoctorAdminProfileResponse), context.SchemaRepository);
        operation.Responses!["200"].Content!["application/json"].Schema = new OpenApiSchema
        {
            OneOf = [publicSchema, adminSchema]
        };
    }
}
