using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;

/// <summary>
/// Asigna un médico para el resumen de la solicitud, cuando el paciente agenda eligiendo solo la especialidad.
/// </summary>
/// <param name="SpecialtyId">Especialidad elegida.</param>
/// <param name="StartsAt">Inicio del horario elegido.</param>
/// <param name="ProvinceId">Filtro de provincia; opcional.</param>
/// <param name="MunicipalityId">Filtro de municipio; opcional.</param>
public sealed record GetDoctorAssignmentQuery(
    int SpecialtyId,
    DateTimeOffset StartsAt,
    int? ProvinceId = null,
    int? MunicipalityId = null) : IQuery<DoctorAssignmentDto>;
