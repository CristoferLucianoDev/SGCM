using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>
/// Estados de cita que abarca cada <see cref="ReportType"/>. Los estados intermedios se agrupan con el que el médico
/// reconoce: una cita confirmada todavía está pendiente de atender y una rechazada tampoco se realizó, igual que una
/// cancelada.
/// </summary>
public static class ReportTypeStatuses
{
    private static readonly AppointmentStatus[] Completed = [AppointmentStatus.Completed];
    private static readonly AppointmentStatus[] Cancelled = [AppointmentStatus.Cancelled, AppointmentStatus.Rejected];
    private static readonly AppointmentStatus[] Pending = [AppointmentStatus.Pending, AppointmentStatus.Confirmed];
    private static readonly AppointmentStatus[] All = Enum.GetValues<AppointmentStatus>();

    /// <summary>Estados de cita que incluye el tipo de reporte.</summary>
    /// <exception cref="ArgumentOutOfRangeException">Si el tipo no está definido.</exception>
    public static IReadOnlyList<AppointmentStatus> Of(ReportType type) => type switch
    {
        ReportType.Completed => Completed,
        ReportType.Cancelled => Cancelled,
        ReportType.Pending => Pending,
        ReportType.All => All,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "El tipo de reporte no está definido.")
    };
}
