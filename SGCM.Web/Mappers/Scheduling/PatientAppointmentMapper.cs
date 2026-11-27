using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.People;

namespace SGCM.Web.Mappers.Scheduling;

/// <summary>
/// Conversión entre los contratos HTTP del historial y el detalle de citas del paciente y los casos de uso.
/// </summary>
public static class PatientAppointmentMapper
{
    private const string EmptyListMessage = "No tienes citas registradas.";
    private const string PendingText = "pending";
    private const string ConfirmedText = "confirmed";
    private const string RejectedText = "rejected";
    private const string CancelledText = "cancelled";
    private const string CompletedText = "completed";
    private const string CancelActionText = "cancel";
    private const string RescheduleActionText = "reschedule";
    private const string ChangeDoctorActionText = "changeDoctor";
    private const string ConfirmActionText = "confirm";
    private const string RejectActionText = "reject";
    private const string ExtendActionText = "extend";
    private const string CompleteActionText = "complete";

    /// <summary>
    /// Convierte los parámetros del historial en la consulta; el filtro de estado pasa al estado del dominio
    /// (<c>null</c> pide todos).
    /// </summary>
    /// <param name="request">Parámetros recibidos.</param>
    /// <returns>La consulta del historial.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si el filtro de estado no es un valor definido.</exception>
    public static GetPatientAppointmentHistoryQuery ToQuery(this ListPatientAppointmentsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new GetPatientAppointmentHistoryQuery(
            request.Status is null ? null : ToDomainStatus(request.Status.Value),
            request.From,
            request.To,
            request.PageNumber,
            request.PageSize);
    }

    /// <summary>
    /// Convierte la solicitud de reprogramación en el comando, con el id de la cita de la ruta. La nueva hora pasa tal
    /// cual, aunque sea nula, para que la valide el caso de uso.
    /// </summary>
    /// <param name="request">Solicitud recibida.</param>
    /// <param name="appointmentId">Id de la cita tomado de la ruta.</param>
    /// <returns>El comando de reprogramación.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static RescheduleAppointmentCommand ToCommand(this RescheduleAppointmentRequest request, int appointmentId)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RescheduleAppointmentCommand(appointmentId, request.StartsAt);
    }

    /// <summary>
    /// Copia la cita al contrato HTTP con el estado en minúsculas.
    /// </summary>
    /// <param name="appointment">Cita que devolvió la consulta.</param>
    /// <returns>El contrato de respuesta.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="appointment"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si el estado de la cita no es un valor definido.</exception>
    public static PatientAppointmentResponse ToResponse(this PatientAppointmentDto appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new PatientAppointmentResponse(
            appointment.AppointmentId,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.AppointmentDate,
            appointment.StartTime,
            appointment.EndTime,
            appointment.DoctorId,
            appointment.DoctorName,
            appointment.SpecialtyId,
            appointment.SpecialtyName,
            ToStatusText(appointment.Status));
    }

    /// <summary>
    /// Convierte la página al contrato HTTP; solo avisa con un mensaje cuando no hay ninguna cita.
    /// </summary>
    /// <param name="page">Página que devolvió la consulta.</param>
    /// <returns>El contrato de respuesta con las citas en el mismo orden.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="page"/> es nulo.</exception>
    public static ListPatientAppointmentsResponse ToResponse(this PagedResult<PatientAppointmentDto> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListPatientAppointmentsResponse(
            page.Items.Select(appointment => appointment.ToResponse()).ToArray(),
            page.PageNumber,
            page.PageSize,
            page.TotalCount,
            page.TotalCount == 0 ? EmptyListMessage : null);
    }

    /// <summary>
    /// Copia el detalle al contrato HTTP conservando los dos motivos, incluso si son nulos, con las acciones
    /// permitidas en camelCase, el tiempo para confirmar en minutos redondeados hacia abajo y el perfil público del
    /// médico con el mismo formato que su perfil, o nulo si el médico está desactivado.
    /// </summary>
    /// <param name="detail">Detalle que devolvió la consulta.</param>
    /// <returns>El contrato de respuesta.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="detail"/> es nulo.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si alguna acción no es un valor definido.</exception>
    public static PatientAppointmentDetailResponse ToResponse(this PatientAppointmentDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        var allowed = detail.AllowedActions;

        return new PatientAppointmentDetailResponse(
            detail.Appointment.ToResponse(),
            detail.ReasonForVisit,
            detail.StatusReason,
            allowed.Actions.Select(ToActionText).ToArray(),
            allowed.ConfirmationDeadline,
            allowed.TimeUntilConfirmationDeadline is { } remaining ? (int)Math.Floor(remaining.TotalMinutes) : null,
            detail.DoctorProfile?.ToResponse());
    }

    private static AppointmentStatus ToDomainStatus(AppointmentStatusFilter filter) => filter switch
    {
        AppointmentStatusFilter.Pending => AppointmentStatus.Pending,
        AppointmentStatusFilter.Confirmed => AppointmentStatus.Confirmed,
        AppointmentStatusFilter.Rejected => AppointmentStatus.Rejected,
        AppointmentStatusFilter.Cancelled => AppointmentStatus.Cancelled,
        AppointmentStatusFilter.Completed => AppointmentStatus.Completed,
        _ => throw new ArgumentOutOfRangeException(
            nameof(ListPatientAppointmentsRequest.Status), filter, "El estado no es válido.")
    };

    private static string ToActionText(AppointmentAction action) => action switch
    {
        AppointmentAction.Cancel => CancelActionText,
        AppointmentAction.Reschedule => RescheduleActionText,
        AppointmentAction.ChangeDoctor => ChangeDoctorActionText,
        AppointmentAction.Confirm => ConfirmActionText,
        AppointmentAction.Reject => RejectActionText,
        AppointmentAction.Extend => ExtendActionText,
        AppointmentAction.Complete => CompleteActionText,
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "La acción no es válida.")
    };

    private static string ToStatusText(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => PendingText,
        AppointmentStatus.Confirmed => ConfirmedText,
        AppointmentStatus.Rejected => RejectedText,
        AppointmentStatus.Cancelled => CancelledText,
        AppointmentStatus.Completed => CompletedText,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "El estado no es válido.")
    };
}
