using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/patients/me/appointments")]
public sealed class PatientAppointmentsController : ApiControllerBase
{
    /// <summary>
    /// Lista el historial de citas del paciente autenticado, paginado.
    /// </summary>
    /// <remarks>
    /// Devuelve las citas propias, pasadas y futuras, de la más reciente a la más antigua. El estado y el rango de
    /// fechas son opcionales; las fechas son locales e incluidas.
    /// Errores de forma de la petición (fecha que no es yyyy-MM-dd, número no entero, estado desconocido)
    /// responden 400 con ValidationProblemDetails, sin code.
    /// Los errores de las reglas responden 400 con code: Appointment.InvalidPageNumber, Appointment.InvalidPageSize,
    /// Appointment.PageOffsetTooLarge, Appointment.InvalidStatus, Appointment.InvalidDateRange o
    /// Appointment.DateOutOfRange.
    /// 404 responde Patient.ProfileNotFound si el usuario aún no completó su perfil de paciente.
    /// </remarks>
    /// <param name="request">Filtros y paginación del historial.</param>
    /// <param name="handler">Manejador de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(typeof(ListPatientAppointmentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] ListPatientAppointmentsRequest request,
        [FromServices] IQueryHandler<GetPatientAppointmentHistoryQuery, PagedResult<PatientAppointmentDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>
    /// Devuelve el detalle de una cita del paciente autenticado.
    /// </summary>
    /// <remarks>
    /// Incluye el motivo de consulta y, si la cita fue cancelada o rechazada, su motivo.
    /// 400 responde Appointment.InvalidAppointmentId si el id no es mayor que cero. Un id que no es entero no
    /// coincide con la ruta y responde 404.
    /// 404 responde Patient.ProfileNotFound sin perfil de paciente y Appointment.NotFound si la cita es ajena o no
    /// existe, sin distinguir entre ambos casos.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="handler">Manejador de la consulta.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpGet("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(typeof(PatientAppointmentDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int id,
        [FromServices] IQueryHandler<GetPatientAppointmentDetailQuery, PatientAppointmentDetailDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetPatientAppointmentDetailQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Reprograma una cita pendiente o confirmada del paciente autenticado a otra hora.</summary>
    /// <remarks>
    /// Solo citas propias pendientes o confirmadas, hasta el plazo configurado
    /// (Scheduling:PatientRescheduleDeadlineHours), con la nueva hora en punto y dentro de la ventana de reservas.
    /// La cita vuelve a pendiente. 400 con code responde Appointment.StartsAtRequired, Appointment.NotOnHour,
    /// Appointment.TooSoon, Appointment.BeyondHorizon o Appointment.SameSchedule. 404 responde Appointment.NotFound si la
    /// cita es ajena o no existe. 409 responde Appointment.NotActive, Appointment.RescheduleDeadlinePassed,
    /// Appointment.SlotUnavailable o Appointment.PatientOverlap. Los errores de forma de la petición (cuerpo mal
    /// formado) responden 400 sin code; un id que no es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Nueva hora de inicio con su desfase.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/reschedule")]
    [Authorize(Roles = nameof(UserRole.Patient))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reschedule(
        int id,
        RescheduleAppointmentRequest request,
        [FromServices] ICommandHandler<RescheduleAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
}
