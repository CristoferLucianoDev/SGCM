using SGCM.Data.Domain.Clinical.MedicalRecords;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Clinical;

internal sealed class MedicalRecordAccessLogRepository(AppDbContext context) : IMedicalRecordAccessLogRepository
{
    public void Add(MedicalRecordAccessLog log) => context.Set<MedicalRecordAccessLog>().Add(log);
}
