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
    // Estados activos de ck_appointment_status: los que ocupan el horario del médico.
    private const string PendingStatus = "pending";
    private const string ConfirmedStatus = "confirmed";

    private static readonly string[] ActiveStatuses = [PendingStatus, ConfirmedStatus];

    // Consulta directa porque el agregado Appointment aún no está mapeado; puede reemplazarse cuando lo esté.
    public async Task<IReadOnlyDictionary<int, IReadOnlyList<BookedInterval>>> ListActiveIntervalsAsync(
        IReadOnlyCollection<int> doctorIds, DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (doctorIds.Count == 0) return new Dictionary<int, IReadOnlyList<BookedInterval>>();

        var windowStart = from.ToUniversalTime();
        var windowEnd = to.ToUniversalTime();
        // Npgsql solo mapea int[] a un arreglo de PostgreSQL, no una colección de solo lectura.
        var ids = doctorIds.ToArray();

        var rows = await context.Database.SqlQuery<ActiveAppointmentRow>($"""
            SELECT id, doctor_id, starts_at, ends_at
            FROM scheduling.appointment
            WHERE doctor_id = ANY({ids})
              AND status = ANY({ActiveStatuses})
              AND starts_at < {windowEnd}
              AND ends_at > {windowStart}
            ORDER BY starts_at, id
            """).ToListAsync(cancellationToken);

        // La base garantiza fin > inicio con ck_appointment_time_range, así que Create no puede fallar.
        return rows
            .GroupBy(row => row.DoctorId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<BookedInterval>)group
                    .Select(row => new BookedInterval(row.Id, TimeRange.Create(row.StartsAt, row.EndsAt).Value))
                    .ToList());
    }
}

/// <summary>Fila de la consulta; los nombres siguen la convención snake_case de las columnas.</summary>
internal sealed record ActiveAppointmentRow(int Id, int DoctorId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
