namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Todas las citas activas del médico en el rango pedido, en orden cronológico.</summary>
public sealed record DoctorCalendarResponse(IReadOnlyList<DoctorScheduleItemResponse> Items);
