using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Clinical.MedicalRecords;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Clinical;

/// <summary>
/// Repositorio de solo inserción del registro clínico. No expone métodos para modificar ni borrar, y la vigente se
/// lee sin rastrear: corregirla crea otra versión con <see cref="MedicalRecord.Correct"/>.
/// </summary>
internal sealed class MedicalRecordRepository(AppDbContext context) : IMedicalRecordRepository
{
    public void Add(MedicalRecord record) => context.Set<MedicalRecord>().Add(record);

    public Task<MedicalRecord?> GetCurrentAsync(int appointmentId, CancellationToken cancellationToken) =>
        context.Set<MedicalRecord>().AsNoTracking()
            .Where(record => record.AppointmentId == appointmentId)
            .OrderByDescending(record => record.CreatedAt)
            .ThenByDescending(record => record.Id)
            .FirstOrDefaultAsync(cancellationToken);
}
