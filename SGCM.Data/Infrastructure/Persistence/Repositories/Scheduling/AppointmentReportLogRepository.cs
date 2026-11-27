using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Scheduling;

internal sealed class AppointmentReportLogRepository(AppDbContext context) : IAppointmentReportLogRepository
{
    public void Add(AppointmentReportLog log) => context.Set<AppointmentReportLog>().Add(log);
}
