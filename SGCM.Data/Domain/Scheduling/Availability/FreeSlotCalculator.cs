namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Servicio de dominio sin estado que calcula los horarios libres de un médico: horas en punto donde
/// cabe una cita completa dentro de un bloque abierto, sin traslaparse con sus citas activas.
/// </summary>
public static class FreeSlotCalculator
{
    /// <summary>
    /// Calcula los instantes de inicio de los horarios libres.
    /// </summary>
    /// <remarks>
    /// La ventana limita cuándo puede EMPEZAR el horario, no cuándo termina: el inicio de la ventana se
    /// incluye (si no está en punto, el primer candidato es la siguiente hora en punto) y su fin se excluye.
    /// "En punto" se mide en UTC. Cada horario debe caber dentro de UN solo bloque: los bloques contiguos
    /// no se unen. Las citas ocupan [inicio, fin), así que una cita contigua no bloquea; una cuyo fin no está
    /// en punto bloquea hasta su fin real.
    /// </remarks>
    /// <param name="openBlocks">Bloques abiertos del médico, en cualquier orden.</param>
    /// <param name="activeAppointments">Citas activas del médico, en cualquier orden.</param>
    /// <param name="searchWindow">Ventana dentro de la cual debe empezar cada horario.</param>
    /// <param name="appointmentDurationHours">Duración de la cita en horas; debe ser positiva.</param>
    /// <param name="excludedAppointmentId">Cita que no bloquea, para reprogramarla sobre su propio horario.</param>
    /// <returns>Inicios en UTC, de menor a mayor y sin duplicados; lista vacía si no hay horarios.</returns>
    /// <exception cref="ArgumentNullException">Si alguna colección o la ventana es nula.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Si la duración es cero o negativa.</exception>
    public static IReadOnlyList<DateTimeOffset> Calculate(
        IEnumerable<TimeRange> openBlocks,
        IEnumerable<BookedInterval> activeAppointments,
        TimeRange searchWindow,
        int appointmentDurationHours,
        int? excludedAppointmentId = null)
    {
        ArgumentNullException.ThrowIfNull(openBlocks);
        ArgumentNullException.ThrowIfNull(activeAppointments);
        ArgumentNullException.ThrowIfNull(searchWindow);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(appointmentDurationHours);

        var duration = TimeSpan.FromHours(appointmentDurationHours);
        var blockers = activeAppointments
            .Where(appointment => appointment.AppointmentId != excludedAppointmentId)
            .ToList();

        // Un conjunto ordenado evita repetir instantes si dos bloques recibidos se traslapan entre sí.
        var starts = new SortedSet<DateTimeOffset>(
            openBlocks
                .SelectMany(block => CandidateStarts(block, searchWindow, duration))
                .Where(candidate => IsFree(candidate, duration, blockers)));

        return starts.ToList();
    }

    /// <summary>Horas en punto dentro de la ventana en las que la cita cabe completa en el bloque.</summary>
    private static IEnumerable<DateTimeOffset> CandidateStarts(TimeRange block, TimeRange window, TimeSpan duration)
    {
        var firstTicks = RoundUpToHour(Math.Max(block.Start.UtcTicks, window.Start.UtcTicks));

        for (var ticks = firstTicks;
             ticks + duration.Ticks <= block.End.UtcTicks && ticks < window.End.UtcTicks;
             ticks += TimeSpan.TicksPerHour)
        {
            yield return new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    /// <summary>Indica si el rango [inicio, inicio + duración) no se traslapa con ninguna cita.</summary>
    private static bool IsFree(DateTimeOffset start, TimeSpan duration, IReadOnlyList<BookedInterval> blockers)
    {
        var end = start + duration;
        return !blockers.Any(appointment => appointment.Range.Overlaps(start, end));
    }

    /// <summary>Devuelve los ticks de la siguiente hora en punto; si ya está en punto, no lo redondea.</summary>
    private static long RoundUpToHour(long ticks)
    {
        var remainder = ticks % TimeSpan.TicksPerHour;
        return remainder == 0 ? ticks : ticks - remainder + TimeSpan.TicksPerHour;
    }
}
