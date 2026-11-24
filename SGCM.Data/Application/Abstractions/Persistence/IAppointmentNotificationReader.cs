using SGCM.Data.Application.Scheduling.Notifications;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>
/// Lee los datos de una cita que usan sus notificaciones, sin cargar los agregados.
/// </summary>
public interface IAppointmentNotificationReader
{
    /// <summary>
    /// Busca la cita en cualquier estado, con su paciente, su médico y su especialidad.
    /// </summary>
    /// <param name="appointmentId">Id de la cita.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Los datos con la hora en la zona local de la agenda, o <c>null</c> si la cita no existe.</returns>
    Task<AppointmentNotificationData?> GetAsync(int appointmentId, CancellationToken cancellationToken);
}
