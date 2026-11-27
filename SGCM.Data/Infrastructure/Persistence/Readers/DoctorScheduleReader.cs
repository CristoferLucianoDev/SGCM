using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Lee la vista <c>scheduling.vw_doctor_schedule</c>. Los filtros se componen con LINQ sobre la consulta de la vista,
/// así que el conteo y la página comparten el mismo WHERE.
/// </summary>
internal sealed class DoctorScheduleReader(AppDbContext context, IOptions<SchedulingOptions> options) : IDoctorScheduleReader
{
    private readonly BusinessCalendar _calendar = BusinessCalendar.ForTimeZone(options.Value.TimeZone);

    public async Task<PagedResult<DoctorScheduleItemDto>> ListAsync(
        int doctorId, DoctorScheduleFilter filter, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var filtered = ApplyFilters(Schedule(doctorId), filter);
        var totalCount = await filtered.CountAsync(cancellationToken);

        var ordered = filter.Sort == DoctorScheduleSort.Descending
            ? filtered.OrderByDescending(row => row.StartsAt).ThenByDescending(row => row.Id)
            : filtered.OrderBy(row => row.StartsAt).ThenBy(row => row.Id);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var rows = await ordered.Skip(offset).Take(pageSize).ToListAsync(cancellationToken);

        return new PagedResult<DoctorScheduleItemDto>(rows.Select(ToDto).ToList(), pageNumber, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<DoctorScheduleItemDto>> ListRangeAsync(
        int doctorId, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var filter = new DoctorScheduleFilter(null, from, to, null, DoctorScheduleSort.Ascending);
        var rows = await ApplyFilters(Schedule(doctorId), filter)
            .OrderBy(row => row.StartsAt).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    private IQueryable<ScheduleRow> Schedule(int doctorId) =>
        context.Database.SqlQuery<ScheduleRow>($"""
            SELECT id, doctor_id, patient_id, patient_first_name, patient_last_name, specialty_id, specialty_name,
                   starts_at, ends_at, status, is_in_progress
            FROM scheduling.vw_doctor_schedule
            WHERE doctor_id = {doctorId}
            """);

    // El rango de fechas locales se convierte a UTC: [inicio de from, inicio del día siguiente a to).
    private IQueryable<ScheduleRow> ApplyFilters(IQueryable<ScheduleRow> query, DoctorScheduleFilter filter)
    {
        if (filter.Status is { } status)
        {
            var stored = status.ToString().ToLowerInvariant();
            query = query.Where(row => row.Status == stored);
        }

        if (filter.From is { } from)
        {
            var lowerUtc = _calendar.ToUtc(from, TimeOnly.MinValue);
            query = query.Where(row => row.StartsAt >= lowerUtc);
        }

        if (filter.To is { } to)
        {
            var upperUtc = _calendar.ToUtc(to.AddDays(1), TimeOnly.MinValue);
            query = query.Where(row => row.StartsAt < upperUtc);
        }

        if (filter.PatientName is { } name)
        {
            // sp_text_normalize quita tildes y pasa a minúsculas, igual que la búsqueda de médicos.
            query = query.Where(row => PostgresFunctions.NormalizeText(row.PatientFirstName.Trim() + " " + row.PatientLastName.Trim())!
                .Contains(PostgresFunctions.NormalizeText(name)!));
        }

        return query;
    }

    private DoctorScheduleItemDto ToDto(ScheduleRow row)
    {
        var start = _calendar.ToLocal(row.StartsAt);
        var end = _calendar.ToLocal(row.EndsAt);

        return new DoctorScheduleItemDto(
            row.Id,
            row.PatientId,
            $"{row.PatientFirstName.Trim()} {row.PatientLastName.Trim()}",
            row.SpecialtyId,
            row.SpecialtyName,
            row.StartsAt,
            row.EndsAt,
            DateOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(end.DateTime),
            Enum.Parse<AppointmentStatus>(row.Status, ignoreCase: true),
            row.IsInProgress);
    }

    // Propiedades con init para que EF pueda componer filtros y orden sobre las columnas de la vista.
    private sealed class ScheduleRow
    {
        public int Id { get; init; }
        public int DoctorId { get; init; }
        public int PatientId { get; init; }
        public string PatientFirstName { get; init; } = null!;
        public string PatientLastName { get; init; } = null!;
        public int SpecialtyId { get; init; }
        public string SpecialtyName { get; init; } = null!;
        public DateTimeOffset StartsAt { get; init; }
        public DateTimeOffset EndsAt { get; init; }
        public string Status { get; init; } = null!;
        public bool IsInProgress { get; init; }
    }
}
