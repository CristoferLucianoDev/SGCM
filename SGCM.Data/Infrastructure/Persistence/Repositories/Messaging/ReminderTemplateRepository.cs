using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Messaging;

internal sealed class ReminderTemplateRepository(AppDbContext context) : IReminderTemplateRepository
{
    public Task<ReminderTemplate?> GetActiveGlobalByNameAsync(string name, CancellationToken cancellationToken) =>
        context.Set<ReminderTemplate>().AsNoTracking()
            .Where(template => template.DoctorId == null && template.IsActive && template.Name == name)
            .OrderBy(template => template.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<ReminderTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        context.Set<ReminderTemplate>().AsNoTracking()
            .SingleOrDefaultAsync(template => template.Id == id, cancellationToken);
}
