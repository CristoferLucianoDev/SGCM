using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Web.Contracts.Scheduling;

namespace SGCM.Web.Mappers.Scheduling;

/// <summary>Conversión entre los contratos HTTP de los reportes de citas y los casos de uso.</summary>
public static class AppointmentReportMapper
{
    private const string EmptyReportMessage = "No hay citas del tipo y el rango de fechas elegidos.";
    private const string PendingText = "pending";
    private const string ConfirmedText = "confirmed";
    private const string RejectedText = "rejected";
    private const string CancelledText = "cancelled";
    private const string CompletedText = "completed";

    /// <summary>
    /// Convierte la solicitud en el comando. Las fechas ausentes pasan como valor por defecto y un valor de enumeración
    /// no definido pasa tal cual, para que los rechace el validador del caso de uso con su código.
    /// </summary>
    /// <param name="request">Solicitud recibida.</param>
    /// <returns>El comando de generación.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    /// <exception cref="ArgumentException">Si falta el tipo, que el modelo de la petición ya exige.</exception>
    public static GenerateAppointmentReportCommand ToCommand(this GenerateAppointmentReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Type is not { } type)
            throw new ArgumentException("El tipo de reporte es obligatorio.", nameof(request));

        var criteria = new ReportCriteria(
            ToDomainType(type),
            request.From ?? default,
            request.To ?? default,
            request.Status is { } status ? ToDomainStatus(status) : null,
            request.PatientName,
            request.SpecialtyId,
            ToDomainSortField(request.SortBy),
            ToDomainSortDirection(request.SortDirection));

        return new GenerateAppointmentReportCommand(criteria, request.PageSize);
    }

    /// <summary>
    /// Copia el reporte al contrato HTTP sin el médico ni ningún otro identificador, con los estados en minúsculas.
    /// Solo avisa con un mensaje cuando el reporte no tiene filas.
    /// </summary>
    /// <param name="report">Reporte que devolvió el caso de uso.</param>
    /// <returns>El contrato de respuesta, con los totales en el orden del ciclo de vida de la cita.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="report"/> es nulo.</exception>
    public static AppointmentReportResponse ToResponse(this AppointmentReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        var rows = report.Rows;

        return new AppointmentReportResponse(
            Enum.GetValues<AppointmentStatus>()
                .Select(status => new AppointmentReportStatusTotalResponse(
                    ToStatusText(status), report.TotalsByStatus.GetValueOrDefault(status)))
                .ToArray(),
            rows.Items.Select(ToResponse).ToArray(),
            rows.PageNumber,
            rows.PageSize,
            rows.TotalCount,
            rows.TotalCount == 0 ? EmptyReportMessage : null);
    }

    private static AppointmentReportRowResponse ToResponse(AppointmentReportRow row) =>
        new(row.Date, row.StartTime, row.PatientName, row.SpecialtyName, ToStatusText(row.Status), row.ReasonForVisit);

    // Un valor no definido conserva su número para que el validador lo rechace con Report.InvalidType.
    private static ReportType ToDomainType(AppointmentReportType type) => type switch
    {
        AppointmentReportType.Completed => ReportType.Completed,
        AppointmentReportType.Cancelled => ReportType.Cancelled,
        AppointmentReportType.Pending => ReportType.Pending,
        AppointmentReportType.All => ReportType.All,
        _ => (ReportType)(int)type
    };

    private static AppointmentStatus ToDomainStatus(AppointmentStatusFilter status) => status switch
    {
        AppointmentStatusFilter.Pending => AppointmentStatus.Pending,
        AppointmentStatusFilter.Confirmed => AppointmentStatus.Confirmed,
        AppointmentStatusFilter.Rejected => AppointmentStatus.Rejected,
        AppointmentStatusFilter.Cancelled => AppointmentStatus.Cancelled,
        AppointmentStatusFilter.Completed => AppointmentStatus.Completed,
        _ => (AppointmentStatus)(int)status
    };

    private static ReportSortField ToDomainSortField(AppointmentReportSortField field) => field switch
    {
        AppointmentReportSortField.Date => ReportSortField.Date,
        AppointmentReportSortField.Patient => ReportSortField.Patient,
        AppointmentReportSortField.Status => ReportSortField.Status,
        _ => (ReportSortField)(int)field
    };

    private static ReportSortDirection ToDomainSortDirection(AppointmentReportSortDirection direction) => direction switch
    {
        AppointmentReportSortDirection.Ascending => ReportSortDirection.Ascending,
        AppointmentReportSortDirection.Descending => ReportSortDirection.Descending,
        _ => (ReportSortDirection)(int)direction
    };

    private static string ToStatusText(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => PendingText,
        AppointmentStatus.Confirmed => ConfirmedText,
        AppointmentStatus.Rejected => RejectedText,
        AppointmentStatus.Cancelled => CancelledText,
        AppointmentStatus.Completed => CompletedText,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "El estado no es válido.")
    };
}
