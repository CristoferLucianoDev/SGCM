namespace SGCM.Data.Application.Scheduling.Commands.RequestAppointment;

/// <summary>Cita pendiente que se creó con la solicitud.</summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="DoctorId">Médico de la cita.</param>
/// <param name="DoctorName">Nombre completo del médico.</param>
/// <param name="ConsultationFee">Tarifa de consulta del médico.</param>
/// <param name="StartsAt">Inicio en UTC.</param>
/// <param name="EndsAt">Fin en UTC.</param>
/// <param name="DoctorChanged">
/// <c>true</c> si el médico asignado en el resumen perdió el horario y el sistema asignó otro.
/// </param>
public sealed record RequestedAppointmentDto(
    int AppointmentId,
    int DoctorId,
    string DoctorName,
    decimal ConsultationFee,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool DoctorChanged);
