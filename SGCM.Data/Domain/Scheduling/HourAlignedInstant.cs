using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling;

/// <summary>
/// Instante en hora en punto, guardado en UTC. Igual que en la base, "en punto" se mide respecto de la
/// época Unix: un instante con desfase de media hora (por ejemplo, UTC+5:30) a las 8:00 no lo está.
/// </summary>
public sealed record HourAlignedInstant
{
    private HourAlignedInstant(DateTimeOffset value) => Value = value;

    /// <summary>El instante en UTC.</summary>
    public DateTimeOffset Value { get; }

    /// <returns>El instante en UTC, o <see cref="SchedulingErrors.NotOnHour"/>.</returns>
    public static Result<HourAlignedInstant> Create(DateTimeOffset value) =>
        IsOnHour(value) ? new HourAlignedInstant(value.ToUniversalTime()) : SchedulingErrors.NotOnHour;

    /// <summary>Indica si el instante está en hora en punto, sin minutos, segundos ni fracciones.</summary>
    public static bool IsOnHour(DateTimeOffset value) => value.UtcTicks % TimeSpan.TicksPerHour == 0;

    /// <summary>Indica si una hora del día está en punto, sin minutos, segundos ni fracciones.</summary>
    public static bool IsOnHour(TimeOnly time) => time.Ticks % TimeSpan.TicksPerHour == 0;

    public static implicit operator DateTimeOffset(HourAlignedInstant instant) => instant.Value;

    public override string ToString() => Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
