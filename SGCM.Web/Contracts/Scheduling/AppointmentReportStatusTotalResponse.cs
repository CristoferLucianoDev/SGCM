namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Cantidad de citas del reporte en un estado.</summary>
/// <param name="Status">Estado en minúsculas: pending, confirmed, rejected, cancelled o completed.</param>
/// <param name="Count">Cantidad de citas en ese estado, cero si no hay.</param>
public sealed record AppointmentReportStatusTotalResponse(string Status, int Count);
