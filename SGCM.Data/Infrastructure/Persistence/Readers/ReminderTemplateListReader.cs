using Microsoft.EntityFrameworkCore;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Infrastructure.Persistence.Readers;

internal sealed class ReminderTemplateListReader(AppDbContext context) : IReminderTemplateListReader
{
    public async Task<PagedResult<ReminderTemplateDto>> ListAsync(
        int doctorId, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Set<ReminderTemplate>().AsNoTracking()
            .Where(template => template.IsActive && (template.DoctorId == null || template.DoctorId == doctorId));

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = checked((int)(((long)pageNumber - 1) * pageSize));
        var items = await query
            .OrderBy(template => template.DoctorId != null)
            .ThenBy(template => template.Name)
            .ThenBy(template => template.Id)
            .Skip(offset).Take(pageSize)
            .Select(template => new ReminderTemplateDto(
                template.Id, template.Name, template.Subject, template.BodyTemplate, template.IsActive,
                template.DoctorId == null))
            .ToListAsync(cancellationToken);

        return new PagedResult<ReminderTemplateDto>(items, pageNumber, pageSize, totalCount);
    }
}
