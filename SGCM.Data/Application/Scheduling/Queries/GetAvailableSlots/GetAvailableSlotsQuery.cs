using SGCM.Data.Application.Abstractions.Messaging;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;

/// <summary>
/// Consulta los horarios libres de un médico. El paciente debe indicar <paramref name="DoctorId"/>; el médico
/// consulta los suyos y, si lo envía, debe ser el propio. El resultado no se pagina porque lo acota el horizonte
/// de reservas y lo consume un calendario.
/// </summary>
/// <param name="DoctorId">Id del médico cuyos horarios se consultan; opcional para un médico.</param>
public sealed record GetAvailableSlotsQuery(int? DoctorId = null) : IQuery<IReadOnlyList<AvailableSlotDto>>;
