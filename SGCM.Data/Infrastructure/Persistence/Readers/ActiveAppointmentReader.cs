using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IActiveAppointmentReader"/> con una consulta directa a
/// <c>scheduling.appointment</c>.
/// </summary>
internal sealed class ActiveAppointmentReader(AppDbContext context) : IActiveAppointmentReader
{
    // Consulta directa porque el agregado Appointment aún no está mapeado; puede reemplazarse cuando lo esté.
    public async Task<IReadOnlyList<BookedInterval>> ListActiveIntervalsAsync(
        int doctorId, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var windowStart = from.ToUniversalTime();
        var windowEnd = to.ToUniversalTime();

        var rows = await context.Database.SqlQuery<ActiveAppointmentRow>($"""
            SELECT id, starts_at, ends_at
            FROM scheduling.appointment
            WHERE doctor_id = {doctorId}
              AND status IN ('pending', 'confirmed')
              AND starts_at < {windowEnd}
              AND ends_at > {windowStart}
            ORDER BY starts_at, id
            """).ToListAsync(cancellationToken);

        // La base garantiza fin > inicio con ck_appointment_time_range, así que Create no puede fallar.
        return rows
            .Select(row => new BookedInterval(row.Id, TimeRange.Create(row.StartsAt, row.EndsAt).Value))
            .ToList();
    }
}

/// <summary>Fila de la consulta; los nombres siguen la convención snake_case de las columnas.</summary>
internal sealed record ActiveAppointmentRow(int Id, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
