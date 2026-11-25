using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Data.Domain.Common;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SGCM.Web.Api.Controllers.People;

[Route("api/doctors")]
public sealed class DoctorSearchController(ILogger<DoctorSearchController> logger) : ApiControllerBase
{
    private static readonly Error SearchFailed =
        Error.Failure("Doctor.SearchFailed", "Ocurrió un error al buscar médicos.");

    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SearchDoctorsResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<IActionResult> Search(
        [FromQuery] SearchDoctorsRequest request,
        [FromServices] IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>> handler,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);
            return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("Fallo al buscar médicos ({ExceptionType}). Petición {TraceId}.",
                exception.GetType().Name, HttpContext.TraceIdentifier);
            return ErrorResult(SearchFailed);
        }
    }
}

internal sealed class DoctorSearchOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.MethodInfo.DeclaringType != typeof(DoctorSearchController)
            || context.MethodInfo.Name != nameof(DoctorSearchController.Search)) return;

        operation.Summary = "Busca médicos en el directorio público.";
        var defaults = new SearchDoctorsRequest();
        foreach (var parameter in operation.Parameters!.OfType<OpenApiParameter>())
        {
            if (parameter.Schema is OpenApiSchema schema)
            {
                schema.Default = parameter.Name switch
                {
                    nameof(SearchDoctorsRequest.PageNumber) => JsonValue.Create(defaults.PageNumber),
                    nameof(SearchDoctorsRequest.PageSize) => JsonValue.Create(defaults.PageSize),
                    _ => schema.Default
                };
            }
            parameter.Name = JsonNamingPolicy.CamelCase.ConvertName(parameter.Name!);
        }

        var badRequest = operation.Responses!["400"];
        badRequest.Description = "Parámetro no convertible (errors por campo) o valor rechazado por la búsqueda (code).";
        var validationSchema = context.SchemaGenerator.GenerateSchema(typeof(ValidationProblemDetails), context.SchemaRepository);
        var mediaType = badRequest.Content!["application/problem+json"];
        mediaType.Schema = new OpenApiSchema
        {
            OneOf =
            [
                new OpenApiSchema { AllOf = [validationSchema], Required = new HashSet<string> { "errors" } },
                new OpenApiSchema
                {
                    AllOf = [mediaType.Schema!],
                    Required = new HashSet<string> { "code" },
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["code"] = new OpenApiSchema { Type = JsonSchemaType.String }
                    }
                }
            ]
        };
    }
}
