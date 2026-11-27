using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>
/// Repositorio del registro de reportes. Solo agrega filas: el registro no se consulta para modificarlo ni se borra.
/// </summary>
public interface IAppointmentReportLogRepository : IRepository<AppointmentReportLog>;
