using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Commands.RequestAppointment;

/// <summary>
/// Solicitud de cita del paciente autenticado. El paciente elige la especialidad, el horario y, si quiere, el
/// médico (<paramref name="DoctorId"/>). Si no lo elige, envía el médico que le asignó el resumen
/// (<paramref name="AssignedDoctorId"/>) o ninguno, y el sistema asigna uno libre.
/// </summary>
/// <param name="SpecialtyId">Especialidad de la cita; obligatoria.</param>
/// <param name="StartsAt">Inicio del horario elegido.</param>
/// <param name="DoctorId">Médico que eligió el paciente; se rechaza si ya no tiene libre el horario.</param>
/// <param name="AssignedDoctorId">
/// Médico que el sistema asignó en el resumen; si ya no tiene libre el horario, se asigna otro y se informa.
/// </param>
/// <param name="ProvinceId">Filtro de provincia para asignar el médico; se ignora con <paramref name="DoctorId"/>.</param>
/// <param name="MunicipalityId">Filtro de municipio para asignar el médico; se ignora con <paramref name="DoctorId"/>.</param>
/// <param name="ReasonForVisit">Motivo de la consulta; opcional.</param>
public sealed record RequestAppointmentCommand(
    int SpecialtyId,
    DateTimeOffset StartsAt,
    int? DoctorId = null,
    int? AssignedDoctorId = null,
    int? ProvinceId = null,
    int? MunicipalityId = null,
    string? ReasonForVisit = null) : ICommand<RequestedAppointmentDto>;
