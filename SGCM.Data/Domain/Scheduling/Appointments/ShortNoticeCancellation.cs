namespace SGCM.Data.Domain.Scheduling.Appointments;

/// <summary>
/// Regla de la cancelación del médico con poco aviso: si faltan menos horas que el umbral para el inicio de la
/// cita, el motivo debe tener un mínimo de palabras. Los valores salen de la configuración
/// (<c>DoctorLateCancellationThresholdHours</c> y <c>DoctorLateCancellationMinimumWords</c>).
/// </summary>
public sealed record ShortNoticeCancellation
{
    /// <summary>Crea la regla con los valores de la configuración.</summary>
    /// <param name="thresholdHours">Horas antes del inicio por debajo de las cuales la cancelación es con poco aviso.</param>
    /// <param name="minimumWords">Palabras mínimas del motivo en una cancelación con poco aviso.</param>
    /// <exception cref="ArgumentOutOfRangeException">Si alguno de los dos valores no es positivo.</exception>
    public ShortNoticeCancellation(int thresholdHours, int minimumWords)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(thresholdHours);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimumWords);

        ThresholdHours = thresholdHours;
        MinimumWords = minimumWords;
    }

    /// <summary>Horas antes del inicio por debajo de las cuales la cancelación es con poco aviso.</summary>
    public int ThresholdHours { get; }

    /// <summary>Palabras mínimas del motivo en una cancelación con poco aviso.</summary>
    public int MinimumWords { get; }

    /// <summary>
    /// Indica si cancelar en <paramref name="now"/> es con poco aviso: faltan menos horas que el umbral, o la
    /// cita ya empezó. A exactamente el umbral no aplica.
    /// </summary>
    /// <param name="startsAt">Inicio de la cita.</param>
    /// <param name="now">Instante actual.</param>
    public bool AppliesTo(DateTimeOffset startsAt, DateTimeOffset now) =>
        startsAt - now < TimeSpan.FromHours(ThresholdHours);
}
