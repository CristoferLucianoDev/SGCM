using System.Globalization;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;

/// <summary>
/// Convierte un <see cref="AppointmentReport"/> en el <see cref="ReportDocument"/> que escriben los exportadores, con
/// los textos en español: el título según el tipo, las columnas de la pantalla y los estados con su nombre.
/// </summary>
internal static class AppointmentReportDocumentFactory
{
    private const string TitlePrefix = "Reporte de citas: ";
    private const string DateFormat = "dd/MM/yyyy";
    private const string TimeFormat = "HH:mm";
    private const string CompletedTypeName = "realizadas";
    private const string CancelledTypeName = "canceladas";
    private const string PendingTypeName = "pendientes";
    private const string AllTypeName = "todas";
    private const string PendingName = "Pendiente";
    private const string ConfirmedName = "Confirmada";
    private const string RejectedName = "Rechazada";
    private const string CancelledName = "Cancelada";
    private const string CompletedName = "Completada";

    /// <summary>Encabezados de la tabla, en el mismo orden que las columnas de la pantalla.</summary>
    public static readonly IReadOnlyList<string> Columns =
        ["Fecha", "Hora", "Paciente", "Especialidad", "Estado", "Motivo de consulta"];

    /// <summary>Arma el documento con todas las filas del reporte, en el mismo orden.</summary>
    /// <param name="report">Reporte completo.</param>
    /// <param name="criteria">Criterios con que se construyó, para el título y el rango.</param>
    /// <param name="generatedAt">Momento de la exportación, en la hora local de la agenda.</param>
    public static ReportDocument Create(AppointmentReport report, ReportCriteria criteria, DateTimeOffset generatedAt)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(criteria);

        return new ReportDocument(
            TitlePrefix + TypeName(criteria.Type),
            criteria.From,
            criteria.To,
            generatedAt,
            Columns,
            report.Rows.Items.Select(ToCells).ToList(),
            Enum.GetValues<AppointmentStatus>()
                .ToDictionary(StatusName, status => report.TotalsByStatus.GetValueOrDefault(status)));
    }

    private static IReadOnlyList<string> ToCells(AppointmentReportRow row) =>
    [
        row.Date.ToString(DateFormat, CultureInfo.InvariantCulture),
        row.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
        row.PatientName,
        row.SpecialtyName,
        StatusName(row.Status),
        row.ReasonForVisit ?? string.Empty
    ];

    private static string TypeName(ReportType type) => type switch
    {
        ReportType.Completed => CompletedTypeName,
        ReportType.Cancelled => CancelledTypeName,
        ReportType.Pending => PendingTypeName,
        ReportType.All => AllTypeName,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "El tipo de reporte no está definido.")
    };

    private static string StatusName(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Pending => PendingName,
        AppointmentStatus.Confirmed => ConfirmedName,
        AppointmentStatus.Rejected => RejectedName,
        AppointmentStatus.Cancelled => CancelledName,
        AppointmentStatus.Completed => CompletedName,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "El estado no está definido.")
    };
}
