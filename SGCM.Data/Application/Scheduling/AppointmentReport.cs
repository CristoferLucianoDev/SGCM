using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling;

/// <summary>Reporte de citas del médico autenticado: los totales por estado y las filas.</summary>
/// <param name="DoctorId">Médico dueño del reporte, para registrarlo.</param>
/// <param name="TotalsByStatus">Cantidad de citas por estado sobre todo el resultado filtrado, con los cinco estados.</param>
/// <param name="Rows">Filas pedidas, con el total de filas del resultado filtrado.</param>
public sealed record AppointmentReport(
    int DoctorId,
    IReadOnlyDictionary<AppointmentStatus, int> TotalsByStatus,
    PagedResult<AppointmentReportRow> Rows);
