using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Especialidad, horario y filtros de ubicación con los que se asigna un médico para el resumen.</summary>
/// <param name="SpecialtyId">Especialidad elegida por el paciente.</param>
/// <param name="StartsAt">Inicio del horario elegido, con su desfase; por ejemplo "2027-01-12T09:00:00-04:00".</param>
/// <param name="ProvinceId">Filtro de provincia; opcional.</param>
/// <param name="MunicipalityId">Filtro de municipio; opcional.</param>
public sealed record DoctorAssignmentRequest(
    [BindRequired] int SpecialtyId,
    [BindRequired] DateTimeOffset StartsAt,
    int? ProvinceId = null,
    int? MunicipalityId = null);
