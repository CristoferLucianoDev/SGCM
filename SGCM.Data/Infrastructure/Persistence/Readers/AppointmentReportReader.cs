using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

/// <summary>
/// Implementación de <see cref="IAppointmentReportReader"/> con consultas de solo lectura sobre
/// <c>scheduling.appointment</c>, unida a paciente y especialidad. La búsqueda por paciente usa la misma
/// normalización de texto que el directorio de médicos, así que no distingue mayúsculas ni tildes.
/// </summary>
internal sealed class AppointmentReportReader(AppDbContext context) : IAppointmentReportReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusAsync(
        AppointmentReportFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var counts = await Filter(filter)
            .GroupBy(appointment => appointment.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(count => count.Status, count => count.Count);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppointmentReportRowData>> ListAsync(
        AppointmentReportFilter filter,
        ReportSortField sortBy,
        ReportSortDirection sortDirection,
        int offset,
        int? limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var ordered = Order(Join(Filter(filter)), sortBy, sortDirection == ReportSortDirection.Descending);
        var page = limit is { } take ? ordered.Skip(offset).Take(take) : ordered.Skip(offset);

        return await page.Select(row => new AppointmentReportRowData(
                row.Appointment.StartsAt,
                row.Patient.FirstName,
                row.Patient.LastName,
                row.Specialty.Name,
                row.Appointment.Status,
                row.Appointment.ReasonForVisit))
            .ToListAsync(cancellationToken);
    }

    // Se compone una sola consulta base para que el conteo y las filas compartan el WHERE.
    private IQueryable<Appointment> Filter(AppointmentReportFilter filter)
    {
        var statuses = filter.Statuses.ToList();
        var query = context.Appointments.AsNoTracking().Where(appointment =>
            appointment.DoctorId == filter.DoctorId
            && statuses.Contains(appointment.Status)
            && appointment.StartsAt >= filter.FromUtc
            && appointment.StartsAt < filter.ToUtcExclusive);

        if (filter.PatientName is { } patientName)
        {
            query = query.Where(appointment => context.Patients.Any(patient =>
                patient.Id == appointment.PatientId
                && PostgresFunctions.NormalizeText(patient.FirstName.Trim() + " " + patient.LastName.Trim())!
                    .Contains(PostgresFunctions.NormalizeText(patientName)!)));
        }

        if (filter.SpecialtyId is { } specialtyId)
        {
            query = query.Where(appointment => appointment.SpecialtyId == specialtyId);
        }

        return query;
    }

    // ReportJoin se crea con inicializador y no con constructor para poder ordenar y paginar sobre él.
    private IQueryable<ReportJoin> Join(IQueryable<Appointment> appointments) =>
        from appointment in appointments
        join patient in context.Patients.AsNoTracking() on appointment.PatientId equals patient.Id
        join specialty in context.Specialties.AsNoTracking() on appointment.SpecialtyId equals specialty.Id
        select new ReportJoin { Appointment = appointment, Patient = patient, Specialty = specialty };

    private static IQueryable<ReportJoin> Order(IQueryable<ReportJoin> rows, ReportSortField sortBy, bool descending) =>
        sortBy switch
        {
            ReportSortField.Patient => ThenByStart(descending
                ? rows.OrderByDescending(row => row.Patient.FirstName).ThenByDescending(row => row.Patient.LastName)
                : rows.OrderBy(row => row.Patient.FirstName).ThenBy(row => row.Patient.LastName), descending),
            ReportSortField.Status => ThenByStart(descending
                ? rows.OrderByDescending(StatusRank)
                : rows.OrderBy(StatusRank), descending),
            _ => descending
                ? rows.OrderByDescending(row => row.Appointment.StartsAt).ThenByDescending(row => row.Appointment.Id)
                : rows.OrderBy(row => row.Appointment.StartsAt).ThenBy(row => row.Appointment.Id)
        };

    private static IQueryable<ReportJoin> ThenByStart(IOrderedQueryable<ReportJoin> rows, bool descending) =>
        descending
            ? rows.ThenByDescending(row => row.Appointment.StartsAt).ThenByDescending(row => row.Appointment.Id)
            : rows.ThenBy(row => row.Appointment.StartsAt).ThenBy(row => row.Appointment.Id);

    // El estado se guarda como texto; ordenarlo por su texto daría un orden alfabético en inglés. Se ordena por el
    // ciclo de vida de la cita, el mismo orden en que se declara AppointmentStatus. Es una expresión y no un método
    // para que EF la traduzca a un CASE en SQL.
    private static readonly Expression<Func<ReportJoin, int>> StatusRank = row =>
        row.Appointment.Status == AppointmentStatus.Pending ? (int)AppointmentStatus.Pending
        : row.Appointment.Status == AppointmentStatus.Confirmed ? (int)AppointmentStatus.Confirmed
        : row.Appointment.Status == AppointmentStatus.Rejected ? (int)AppointmentStatus.Rejected
        : row.Appointment.Status == AppointmentStatus.Cancelled ? (int)AppointmentStatus.Cancelled
        : (int)AppointmentStatus.Completed;

    private sealed record ReportJoin
    {
        public required Appointment Appointment { get; init; }

        public required Patient Patient { get; init; }

        public required Specialty Specialty { get; init; }
    }
}
