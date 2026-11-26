namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Lo que quien consulta una cita puede hacer con ella en un instante dado, y los datos que acompañan esas acciones.
/// </summary>
/// <param name="Actions">Acciones disponibles, en el orden de <see cref="AppointmentAction"/>.</param>
/// <param name="RequiresExtendedCancellationReason">
/// <c>true</c> si cancelar exige un motivo con un mínimo de palabras, por ser una cancelación del médico con poco aviso.
/// </param>
/// <param name="ConfirmationDeadline">Límite para confirmar o rechazar; <c>null</c> si la cita no está pendiente.</param>
/// <param name="TimeUntilConfirmationDeadline">
/// Tiempo que falta para el límite, sin bajar de cero; <c>null</c> si la cita no está pendiente.
/// </param>
public sealed record AllowedAppointmentActions(
    IReadOnlyList<AppointmentAction> Actions,
    bool RequiresExtendedCancellationReason,
    DateTimeOffset? ConfirmationDeadline,
    TimeSpan? TimeUntilConfirmationDeadline);
