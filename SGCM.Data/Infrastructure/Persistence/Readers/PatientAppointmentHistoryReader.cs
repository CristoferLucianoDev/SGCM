using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IPatientAppointmentHistoryReader"/> con consultas de solo lectura sobre
/// <c>scheduling.appointment</c>, unida a médico y especialidad, tanto para la lista como para el detalle. Las
/// fechas del filtro se interpretan en la zona de <see cref="SchedulingOptions"/>, la misma de la agenda, para que
/// coincidan con lo que el paciente ve.
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

    /// <inheritdoc />
    public async Task<PatientAppointmentDetailData?> GetAsync(
        int patientId, int appointmentId, CancellationToken cancellationToken)
    {
        var calendar = BusinessCalendar.ForTimeZone(options.Value.TimeZone);
        // El filtro por paciente va en la misma consulta: una cita ajena no se distingue de una inexistente.
        var owned = context.Appointments.AsNoTracking()
            .Where(appointment => appointment.PatientId == patientId && appointment.Id == appointmentId);
        var row = await ProjectRows(JoinAppointments(owned)).SingleOrDefaultAsync(cancellationToken);
        if (row is null) return null;

        // La misma proyección del perfil del médico; un médico desactivado no tiene perfil público.
        var doctorProfile = await DoctorPublicProfileProjection.GetAsync(context, row.DoctorId, cancellationToken);
        return ToDetailData(row, calendar, doctorProfile);
    }

    // El médico y la especialidad se unen por clave para que la lectura traiga todo en una sola consulta.
    // AppointmentJoin se crea con inicializador y no con constructor para poder ordenar y paginar sobre él.
    private IQueryable<AppointmentJoin> JoinAppointments(IQueryable<Appointment> appointments) =>
        from appointment in appointments
        join doctor in context.Doctors.AsNoTracking() on appointment.DoctorId equals doctor.Id
        join specialty in context.Specialties.AsNoTracking() on appointment.SpecialtyId equals specialty.Id
        select new AppointmentJoin { Appointment = appointment, Doctor = doctor, Specialty = specialty };

    // El orden y la página van antes de construir HistoryRow porque EF no traduce miembros de un tipo creado
    // con constructor.
    private async Task<List<HistoryRow>> ReadRowsAsync(
        IQueryable<Appointment> appointments, int offset, int pageSize, CancellationToken cancellationToken)
    {
        var page = JoinAppointments(appointments)
            .OrderByDescending(row => row.Appointment.StartsAt).ThenByDescending(row => row.Appointment.Id)
            .Skip(offset).Take(pageSize);

        return await ProjectRows(page).ToListAsync(cancellationToken);
    }

    // Proyección única de la fila; la construcción con constructor debe ser el último operador de la consulta.
    private static IQueryable<HistoryRow> ProjectRows(IQueryable<AppointmentJoin> joined) =>
        joined.Select(row => new HistoryRow(
            row.Appointment.Id,
            row.Appointment.StartsAt,
            row.Appointment.EndsAt,
            row.Appointment.Status,
            row.Doctor.Id,
            row.Doctor.FirstName,
            row.Doctor.LastName,
            row.Specialty.Id,
            row.Specialty.Name,
            row.Appointment.ReasonForVisit,
            row.Appointment.StatusReason));

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

    // El motivo de estado solo tiene sentido en citas canceladas o rechazadas.
    private static PatientAppointmentDetailData ToDetailData(
        HistoryRow row, BusinessCalendar calendar, DoctorPublicProfileDto? doctorProfile)
    {
        var statusReason = row.Status is AppointmentStatus.Cancelled or AppointmentStatus.Rejected
            ? row.StatusReason
            : null;

        return new PatientAppointmentDetailData(ToDto(row, calendar), row.ReasonForVisit, statusReason, doctorProfile);
    }

    private sealed record AppointmentJoin
    {
        public required Appointment Appointment { get; init; }

        public required Doctor Doctor { get; init; }

        public required Specialty Specialty { get; init; }
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
        string SpecialtyName,
        string? ReasonForVisit,
        string? StatusReason);
}
