using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Contracts.Scheduling;

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
    /// Copia el detalle al contrato HTTP conservando los dos motivos, incluso si son nulos.
    /// </summary>
    /// <param name="detail">Detalle que devolvió la consulta.</param>
    /// <returns>El contrato de respuesta.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="detail"/> es nulo.</exception>
    public static PatientAppointmentDetailResponse ToResponse(this PatientAppointmentDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);

        return new PatientAppointmentDetailResponse(
            detail.Appointment.ToResponse(), detail.ReasonForVisit, detail.StatusReason);
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
