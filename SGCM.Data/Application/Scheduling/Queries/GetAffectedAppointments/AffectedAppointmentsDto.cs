namespace SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;

/// <summary>Citas que se cancelarían y lo que se exige del motivo.</summary>
/// <param name="Count">Cantidad de citas activas que se cancelarían.</param>
/// <param name="IsReasonRequired">Si el médico debe escribir un motivo.</param>
/// <param name="IsShortNotice">Si alguna empieza pronto y el motivo debe ser extendido; es la advertencia.</param>
/// <param name="ShortNoticeThresholdHours">Horas del umbral de cancelación tardía.</param>
/// <param name="MinimumShortNoticeWords">Palabras mínimas del motivo extendido.</param>
public sealed record AffectedAppointmentsDto(
    int Count,
    bool IsReasonRequired,
    bool IsShortNotice,
    int ShortNoticeThresholdHours,
    int MinimumShortNoticeWords);
