namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Cita pendiente creada con la solicitud.</summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="DoctorId">Médico de la cita.</param>
/// <param name="DoctorName">Nombre completo del médico.</param>
/// <param name="ConsultationFee">Tarifa de consulta del médico.</param>
/// <param name="StartsAt">Inicio en UTC.</param>
/// <param name="EndsAt">Fin en UTC.</param>
/// <param name="DoctorChanged"><c>true</c> si el médico del resumen perdió el horario y se asignó otro.</param>
public sealed record RequestAppointmentResponse(
    int AppointmentId,
    int DoctorId,
    string DoctorName,
    decimal ConsultationFee,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    bool DoctorChanged);
