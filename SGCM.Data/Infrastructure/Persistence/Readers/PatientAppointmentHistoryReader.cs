using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IPatientAppointmentHistoryReader"/> con una consulta de solo lectura sobre
/// <c>scheduling.appointment</c>, unida a médico y especialidad. Las fechas del filtro se interpretan en la zona
/// de <see cref="SchedulingOptions"/>, la misma de la agenda, para que coincidan con lo que el paciente ve.
/// </summary>
internal sealed class PatientAppointmentHistoryReader(AppDbContext context, IOptions<SchedulingOptions> options)
    : IPatientAppointmentHistoryReader
{
    public async Task<PagedResult<PatientAppointmentDto>> ListAsync(
        int patientId,
        AppointmentStatus? status,
        DateOnly? from,
        DateOnly? to,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var calendar = BusinessCalendar.ForTimeZone(options.Value.TimeZone);
        var filtered = ApplyFilters(
            context.Appointments.AsNoTracking().Where(appointment => appointment.PatientId == patientId),
            status, from, to, calendar);
        var totalCount = await filtered.CountAsync(cancellationToken);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var rows = await ReadRowsAsync(filtered, offset, pageSize, cancellationToken);
        var items = rows.Select(row => ToDto(row, calendar)).ToList();

        return new PagedResult<PatientAppointmentDto>(items, pageNumber, pageSize, totalCount);
    }

    // Se compone sobre la misma consulta para que el conteo y la página compartan el WHERE.
    // El rango de fechas locales se convierte a UTC: [inicio de from, inicio del día siguiente a to).
    private static IQueryable<Appointment> ApplyFilters(
        IQueryable<Appointment> query, AppointmentStatus? status, DateOnly? from, DateOnly? to, BusinessCalendar calendar)
    {
        if (status.HasValue)
        {
            query = query.Where(appointment => appointment.Status == status.Value);
        }

        if (from.HasValue)
        {
            var lowerUtc = calendar.ToUtc(from.Value, TimeOnly.MinValue);
            query = query.Where(appointment => appointment.StartsAt >= lowerUtc);
        }

        if (to.HasValue)
        {
            var upperUtc = calendar.ToUtc(to.Value.AddDays(1), TimeOnly.MinValue);
            query = query.Where(appointment => appointment.StartsAt < upperUtc);
        }

        return query;
    }

    // El médico y la especialidad se unen por clave para que la página traiga todo en una sola consulta.
    // El orden y la página van antes de construir HistoryRow porque EF no traduce miembros de un tipo creado
    // con constructor.
    private async Task<List<HistoryRow>> ReadRowsAsync(
        IQueryable<Appointment> appointments, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var joined =
            from appointment in appointments
            join doctor in context.Doctors.AsNoTracking() on appointment.DoctorId equals doctor.Id
            join specialty in context.Specialties.AsNoTracking() on appointment.SpecialtyId equals specialty.Id
            select new { appointment, doctor, specialty };

        return await joined
            .OrderByDescending(row => row.appointment.StartsAt).ThenByDescending(row => row.appointment.Id)
            .Skip(offset).Take(pageSize)
            .Select(row => new HistoryRow(
                row.appointment.Id,
                row.appointment.StartsAt,
                row.appointment.EndsAt,
                row.appointment.Status,
                row.doctor.Id,
                row.doctor.FirstName,
                row.doctor.LastName,
                row.specialty.Id,
                row.specialty.Name))
            .ToListAsync(cancellationToken);
    }

    private static PatientAppointmentDto ToDto(HistoryRow row, BusinessCalendar calendar)
    {
        var start = calendar.ToLocal(row.StartsAt);
        var end = calendar.ToLocal(row.EndsAt);

        return new PatientAppointmentDto(
            row.AppointmentId,
            row.StartsAt,
            row.EndsAt,
            DateOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(end.DateTime),
            row.DoctorId,
            $"{row.DoctorFirstName.Trim()} {row.DoctorLastName.Trim()}",
            row.SpecialtyId,
            row.SpecialtyName,
            row.Status);
    }

    private sealed record HistoryRow(
        int AppointmentId,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        AppointmentStatus Status,
        int DoctorId,
        string DoctorFirstName,
        string DoctorLastName,
        int SpecialtyId,
        string SpecialtyName);
}
