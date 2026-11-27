using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/doctors/me/appointment-reports")]
public sealed class AppointmentReportsController : ApiControllerBase
{
    /// <summary>Genera el reporte de citas del médico autenticado y lo deja en el registro de reportes.</summary>
    /// <remarks>
    /// Devuelve los totales por estado sobre todo el reporte y la primera página de filas, con fecha y hora locales y
    /// sin identificadores internos. Solo incluye citas del médico autenticado. Si no hay citas del tipo y rango
    /// elegidos, responde 200 con las filas vacías, los totales en cero y un mensaje.
    /// Errores de forma de la petición (tipo ausente, nombre desconocido, fecha que no es yyyy-MM-dd) responden 400
    /// con ValidationProblemDetails, sin code.
    /// Los errores de las reglas responden 400 con code: Report.DateRangeRequired, Report.InvalidDateRange,
    /// Report.InvalidType, Report.InvalidStatus, Report.InvalidSortField, Report.InvalidSortDirection,
    /// Report.PatientNameTooLong, Report.InvalidSpecialtyId o Report.InvalidPageSize.
    /// 404 responde Doctor.NotFound si el médico no tiene perfil activo.
    /// </remarks>
    /// <param name="request">Tipo, rango, filtros, orden y tamaño de la primera página.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(AppointmentReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Generate(
        GenerateAppointmentReportRequest request,
        [FromServices] ICommandHandler<GenerateAppointmentReportCommand, AppointmentReport> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Filtra y ordena el reporte de citas del médico autenticado y devuelve la página pedida.</summary>
    /// <remarks>
    /// Combina los filtros por estado, rango de fechas, paciente y especialidad, y ordena por fecha, paciente o estado
    /// en cualquier sentido. Devuelve los totales por estado sobre todo el resultado filtrado y la página pedida, con
    /// las mismas filas que generaría el reporte con esos criterios. A diferencia de generar, no deja una fila en el
    /// registro de reportes. Si ningún resultado coincide, responde 200 con las filas vacías, los totales en cero y
    /// un mensaje.
    /// Errores de forma de la petición (tipo ausente, nombre desconocido, fecha que no es yyyy-MM-dd, número no
    /// entero) responden 400 con ValidationProblemDetails, sin code.
    /// Los errores de las reglas responden 400 con code: Report.DateRangeRequired, Report.InvalidDateRange,
    /// Report.InvalidType, Report.InvalidStatus, Report.InvalidSortField, Report.InvalidSortDirection,
    /// Report.PatientNameTooLong, Report.InvalidSpecialtyId, Report.InvalidPageNumber, Report.InvalidPageSize o
    /// Report.PageOffsetTooLarge.
    /// 404 responde Doctor.NotFound si el médico no tiene perfil activo.
    /// </remarks>
    /// <param name="request">Tipo, rango, filtros, orden y paginación.</param>
    /// <param name="handler">Manejador de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(AppointmentReportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        [FromQuery] GetAppointmentReportRequest request,
        [FromServices] IQueryHandler<GetAppointmentReportQuery, AppointmentReport> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
