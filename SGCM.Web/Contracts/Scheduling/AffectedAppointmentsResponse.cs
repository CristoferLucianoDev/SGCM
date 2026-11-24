namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Aviso antes de cerrar o reprogramar un bloque: cuántas citas se cancelarían y qué se exige del motivo.</summary>
/// <param name="Count">Cantidad de citas activas que se cancelarían.</param>
/// <param name="IsReasonRequired">Si el médico debe escribir un motivo.</param>
/// <param name="IsShortNotice">Si alguna cita empieza pronto: la web muestra la advertencia y exige el motivo extendido.</param>
/// <param name="ShortNoticeThresholdHours">Horas del umbral de cancelación tardía.</param>
/// <param name="MinimumShortNoticeWords">Palabras mínimas del motivo extendido.</param>
public sealed record AffectedAppointmentsResponse(
    int Count,
    bool IsReasonRequired,
    bool IsShortNotice,
    int ShortNoticeThresholdHours,
    int MinimumShortNoticeWords);
