using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Implementación de <see cref="IAppointmentReportBuilder"/>. El médico sale siempre del usuario autenticado, así que
/// un reporte nunca incluye citas de otro médico. Las fechas del rango se interpretan en el calendario de la agenda y
/// las filas se devuelven en su hora local.
/// </summary>
internal sealed class AppointmentReportBuilder(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IAppointmentReportReader reader,
    BusinessCalendar calendar) : IAppointmentReportBuilder
{
    private const int FirstPage = 1;

    /// <inheritdoc />
    public async Task<Result<AppointmentReport>> BuildPageAsync(
        ReportCriteria criteria, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var doctorId = await ResolveDoctorIdAsync(cancellationToken);
        if (doctorId.IsFailure) return doctorId.Error;

        var filter = ToFilter(doctorId.Value, criteria);
        var totals = await CountTotalsAsync(filter, cancellationToken);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var rows = await reader.ListAsync(
            filter, criteria.SortBy, criteria.SortDirection, offset, pageSize, cancellationToken);

        return new AppointmentReport(doctorId.Value, totals,
            new PagedResult<AppointmentReportRow>(ToRows(rows), pageNumber, pageSize, totals.Values.Sum()));
    }

    /// <inheritdoc />
    public async Task<Result<AppointmentReport>> BuildAllAsync(ReportCriteria criteria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(criteria);
        var doctorId = await ResolveDoctorIdAsync(cancellationToken);
        if (doctorId.IsFailure) return doctorId.Error;

        var filter = ToFilter(doctorId.Value, criteria);
        var totals = await CountTotalsAsync(filter, cancellationToken);
        var rows = ToRows(await reader.ListAsync(
            filter, criteria.SortBy, criteria.SortDirection, offset: 0, limit: null, cancellationToken));

        return new AppointmentReport(doctorId.Value, totals,
            new PagedResult<AppointmentReportRow>(rows, FirstPage, rows.Count, totals.Values.Sum()));
    }

    private async Task<Result<int>> ResolveDoctorIdAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return ReportErrors.DoctorOnly;

        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        return doctorId is null ? DoctorErrors.NotFound : doctorId.Value;
    }

    // El filtro de estado se cruza con los estados del tipo: un estado fuera del tipo deja el reporte vacío.
    // El rango de fechas locales se convierte a UTC como [inicio de From, inicio del día siguiente a To).
    private AppointmentReportFilter ToFilter(int doctorId, ReportCriteria criteria)
    {
        var statuses = ReportTypeStatuses.Of(criteria.Type)
            .Where(status => criteria.Status is null || status == criteria.Status)
            .ToArray();
        var patientName = string.IsNullOrWhiteSpace(criteria.PatientName) ? null : criteria.PatientName.Trim();

        return new AppointmentReportFilter(
            doctorId,
            statuses,
            calendar.ToUtc(criteria.From, TimeOnly.MinValue),
            calendar.ToUtc(criteria.To.AddDays(1), TimeOnly.MinValue),
            patientName,
            criteria.SpecialtyId);
    }

    // Los cinco estados aparecen siempre, con cero si no hay citas, para que la tabla de totales sea estable.
    private async Task<IReadOnlyDictionary<AppointmentStatus, int>> CountTotalsAsync(
        AppointmentReportFilter filter, CancellationToken cancellationToken)
    {
        var counts = await reader.CountByStatusAsync(filter, cancellationToken);
        return Enum.GetValues<AppointmentStatus>()
            .ToDictionary(status => status, status => counts.GetValueOrDefault(status));
    }

    private List<AppointmentReportRow> ToRows(IReadOnlyList<AppointmentReportRowData> rows) =>
        rows.Select(ToRow).ToList();

    private AppointmentReportRow ToRow(AppointmentReportRowData row)
    {
        var start = calendar.ToLocal(row.StartsAt);

        return new AppointmentReportRow(
            DateOnly.FromDateTime(start.DateTime),
            TimeOnly.FromDateTime(start.DateTime),
            $"{row.PatientFirstName.Trim()} {row.PatientLastName.Trim()}",
            row.SpecialtyName,
            row.Status,
            row.ReasonForVisit);
    }
}
