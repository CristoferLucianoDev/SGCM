using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;
using SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;
using SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;
using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorCalendar;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;

namespace SGCM.Web.Api.Controllers.Scheduling;

[Route("api/doctors/me/appointments")]
public sealed class DoctorAppointmentsController : ApiControllerBase
{
    /// <summary>Cancela una cita pendiente o confirmada del médico autenticado.</summary>
    /// <remarks>
    /// Se permite en cualquier momento y siempre con motivo. Dentro del umbral de poco aviso configurado
    /// (Scheduling:DoctorLateCancellationThresholdHours) el motivo debe tener el mínimo de palabras configurado
    /// (Scheduling:DoctorLateCancellationMinimumWords); si no, responde 400. Una cita ajena o inexistente
    /// responde 404; una cita rechazada, cancelada o completada responde 409. El cuerpo es obligatorio.
    /// </remarks>
    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        int id,
        CancelAppointmentByDoctorRequest request,
        [FromServices] ICommandHandler<CancelAppointmentByDoctorCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>Confirma una cita pendiente del médico autenticado.</summary>
    /// <remarks>
    /// Solo citas propias y pendientes, y hasta el plazo de confirmación configurado
    /// (Scheduling:ConfirmationDeadlineHours). 409 responde Appointment.NotPending si la cita no está pendiente y
    /// Appointment.ConfirmationDeadlinePassed si venció el plazo. Una cita ajena o inexistente responde 404.
    /// Appointment.InvalidAppointmentId responde 400 con code si el id no es mayor que cero. Un id que no es entero no
    /// coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(
        int id,
        [FromServices] ICommandHandler<ConfirmAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ConfirmAppointmentCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>Rechaza una cita pendiente del médico autenticado.</summary>
    /// <remarks>
    /// Solo citas propias y pendientes, y hasta el plazo de confirmación configurado
    /// (Scheduling:ConfirmationDeadlineHours). El motivo es obligatorio, hasta 500 caracteres: 400 responde
    /// Appointment.StatusReasonRequired si falta y Appointment.StatusReasonTooLong si es muy largo.
    /// 409 responde Appointment.NotPending si la cita no está pendiente y Appointment.ConfirmationDeadlinePassed si
    /// venció el plazo. Una cita ajena o inexistente responde 404. Los errores de forma de la petición (cuerpo mal
    /// formado) responden 400 sin code; un id que no es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Motivo del rechazo.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/reject")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reject(
        int id,
        RejectAppointmentRequest request,
        [FromServices] ICommandHandler<RejectAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>Reprograma una cita pendiente o confirmada del médico autenticado a otra hora.</summary>
    /// <remarks>
    /// Solo citas propias pendientes o confirmadas que no hayan empezado. La nueva hora debe ser futura, en punto y
    /// dentro del horizonte de reservas, en un horario libre de sus bloques abiertos. La cita queda confirmada y el
    /// paciente recibe el aviso. 400 con code responde Appointment.StartsAtRequired, Appointment.NewStartNotInFuture,
    /// Appointment.NotOnHour, Appointment.BeyondHorizon o Appointment.SameSchedule. 404 responde Appointment.NotFound si
    /// la cita es ajena o no existe. 409 responde Appointment.NotActive, Appointment.AlreadyStarted,
    /// Appointment.SlotUnavailable o Appointment.PatientOverlap. Los errores de forma de la petición (cuerpo mal
    /// formado) responden 400 sin code; un id que no es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Nueva hora de inicio con su desfase.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/reschedule")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reschedule(
        int id,
        RescheduleAppointmentByDoctorRequest request,
        [FromServices] ICommandHandler<RescheduleAppointmentByDoctorCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>Extiende una cita confirmada en curso del médico autenticado.</summary>
    /// <remarks>
    /// Solo citas propias confirmadas que están en curso. Suma horas enteras (entre 1 y 24) al fin actual:
    /// Appointment.InvalidAdditionalHours responde 400 con code si están fuera de ese rango.
    /// 409 responde Appointment.NotInProgress si la cita no está en curso, Appointment.ExtensionUnavailable si la
    /// cita extendida sale del bloque del médico o el tramo nuevo choca con otra cita, y
    /// Appointment.SlotUnavailable si otra reserva tomó el tramo a la vez o el bloque cambió mientras se extendía.
    /// Una cita ajena o inexistente responde 404. Los errores de forma de la petición (cuerpo mal formado)
    /// responden 400 sin code; un id que no es entero no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="request">Horas que se suman al fin de la cita.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/extend")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Extend(
        int id,
        ExtendAppointmentRequest request,
        [FromServices] ICommandHandler<ExtendAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }

    /// <summary>Termina una cita confirmada en curso del médico autenticado.</summary>
    /// <remarks>
    /// Solo citas propias confirmadas que están en curso; la hora actual queda como fin real de la cita.
    /// 409 responde Appointment.NotInProgress si la cita no está en curso. Una cita ajena o inexistente responde 404.
    /// Appointment.InvalidAppointmentId responde 400 con code si el id no es mayor que cero. Un id que no es entero
    /// no coincide con la ruta y responde 404.
    /// </remarks>
    /// <param name="id">Id de la cita, mayor que cero.</param>
    /// <param name="handler">Manejador del comando.</param>
    /// <param name="cancellationToken">Token de cancelación de la petición.</param>
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(
        int id,
        [FromServices] ICommandHandler<CompleteAppointmentCommand, Unit> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CompleteAppointmentCommand(id), cancellationToken);

        return result.IsSuccess ? NoContent() : ErrorResult(result.Error);
    }
    /// <summary>Agenda paginada del médico autenticado: sus citas pendientes y confirmadas.</summary>
    /// <remarks>
    /// Los filtros por estado, rango de fechas locales y nombre del paciente (parcial, sin mayúsculas ni tildes) se
    /// combinan. Ordena por fecha en el sentido pedido.
    /// </remarks>
    [HttpGet]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(DoctorScheduleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] DoctorScheduleRequest request,
        [FromServices] IQueryHandler<GetDoctorScheduleQuery, PagedResult<DoctorScheduleItemDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(request.ToQuery(), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Todas las citas activas del médico entre dos fechas locales, sin paginar, para el calendario.</summary>
    /// <remarks>Ambas fechas se incluyen y el rango abarca como máximo 31 días: el día, la semana o el mes completos.</remarks>
    [HttpGet("calendar")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(DoctorCalendarResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Calendar(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromServices] IQueryHandler<GetDoctorCalendarQuery, IReadOnlyList<DoctorScheduleItemDto>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDoctorCalendarQuery(from, to), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToCalendarResponse()) : ErrorResult(result.Error);
    }

    /// <summary>Detalle de una cita propia con el motivo de consulta y el contacto del paciente.</summary>
    /// <remarks>Una cita de otro médico, o que ya no está en la agenda, responde 404.</remarks>
    [HttpGet("{id:int}")]
    [Authorize(Roles = nameof(UserRole.Doctor))]
    [ProducesResponseType(typeof(DoctorAppointmentDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        int id,
        [FromServices] IQueryHandler<GetDoctorAppointmentDetailQuery, DoctorAppointmentDetailDto> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetDoctorAppointmentDetailQuery(id), cancellationToken);

        return result.IsSuccess ? Ok(result.Value.ToResponse()) : ErrorResult(result.Error);
    }
}
