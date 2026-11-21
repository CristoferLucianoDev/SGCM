using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;

/// <summary>
/// Consulta los horarios libres. El paciente envía <paramref name="DoctorId"/> o, si no elige médico,
/// <paramref name="SpecialtyId"/> con filtros de ubicación opcionales; el médico consulta los suyos y, si envía
/// su <paramref name="DoctorId"/>, debe ser el propio. El resultado no se pagina porque lo acota el horizonte de
/// reservas y lo consume un calendario.
/// </summary>
/// <param name="DoctorId">Id del médico cuyos horarios se consultan; opcional para un médico.</param>
/// <param name="SpecialtyId">
/// Especialidad de la que se buscan horarios cuando el paciente no elige médico; se ignora si envía
/// <paramref name="DoctorId"/> y también la ignora el médico.
/// </param>
/// <param name="ProvinceId">Filtro de provincia de los médicos de la especialidad; se ignora igual que <paramref name="SpecialtyId"/>.</param>
/// <param name="MunicipalityId">Filtro de municipio de los médicos de la especialidad; se ignora igual que <paramref name="SpecialtyId"/>.</param>
public sealed record GetAvailableSlotsQuery(
    int? DoctorId = null,
    int? SpecialtyId = null,
    int? ProvinceId = null,
    int? MunicipalityId = null) : IQuery<IReadOnlyList<AvailableSlotDto>>;
