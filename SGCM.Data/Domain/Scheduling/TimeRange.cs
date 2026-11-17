using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Intervalo [inicio, fin) en UTC con el fin posterior al inicio. Como en la base, el fin no forma
/// parte del rango: dos rangos contiguos no se traslapan.
/// </summary>
public sealed record TimeRange
{
    private TimeRange(DateTimeOffset start, DateTimeOffset end)
    {
        Start = start;
        End = end;
    }

    /// <summary>Inicio en UTC, incluido.</summary>
    public DateTimeOffset Start { get; }

    /// <summary>Fin en UTC, excluido.</summary>
    public DateTimeOffset End { get; }

    public TimeSpan Duration => End - Start;

    /// <returns>El rango en UTC, o <see cref="SchedulingErrors.InvalidTimeRange"/> si el fin no es posterior al inicio.</returns>
    public static Result<TimeRange> Create(DateTimeOffset start, DateTimeOffset end) =>
        IsValid(start, end)
            ? new TimeRange(start.ToUniversalTime(), end.ToUniversalTime())
            : SchedulingErrors.InvalidTimeRange;

    /// <summary>Indica si el fin es posterior al inicio.</summary>
    public static bool IsValid(DateTimeOffset start, DateTimeOffset end) => end > start;

    /// <summary>Indica si los dos rangos comparten algún instante.</summary>
    public bool Overlaps(TimeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Overlaps(other.Start, other.End);
    }

    /// <summary>Indica si el rango comparte algún instante con [inicio, fin).</summary>
    public bool Overlaps(DateTimeOffset start, DateTimeOffset end) => Start < end && start < End;

    /// <summary>Indica si el otro rango cabe completo dentro de este, como una cita dentro de un bloque.</summary>
    public bool Contains(TimeRange other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Start <= other.Start && other.End <= End;
    }

    public override string ToString() =>
        $"[{Start.ToString("O", System.Globalization.CultureInfo.InvariantCulture)}, " +
        $"{End.ToString("O", System.Globalization.CultureInfo.InvariantCulture)})";
}
