namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Solicitud de cita del paciente autenticado.</summary>
/// <param name="SpecialtyId">Especialidad de la cita; obligatoria.</param>
/// <param name="StartsAt">Inicio del horario elegido, con su desfase; por ejemplo "2027-01-12T09:00:00-04:00".</param>
/// <param name="DoctorId">Médico que eligió el paciente; si ya no tiene libre el horario, la reserva se rechaza.</param>
/// <param name="AssignedDoctorId">
/// Médico que asignó el resumen; si ya no tiene libre el horario, el sistema asigna otro e informa el cambio.
/// </param>
/// <param name="ProvinceId">Filtro de provincia para asignar el médico; opcional.</param>
/// <param name="MunicipalityId">Filtro de municipio para asignar el médico; opcional.</param>
/// <param name="ReasonForVisit">Motivo de la consulta; opcional, hasta 500 caracteres.</param>
public sealed record RequestAppointmentRequest(
    int SpecialtyId,
    DateTimeOffset StartsAt,
    int? DoctorId = null,
    int? AssignedDoctorId = null,
    int? ProvinceId = null,
    int? MunicipalityId = null,
    string? ReasonForVisit = null);
