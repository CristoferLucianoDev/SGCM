using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Messaging.Reminders;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Messaging;

internal sealed class ReminderRepository(AppDbContext context) : IReminderRepository
{
    public void Add(Reminder reminder) => context.Set<Reminder>().Add(reminder);

    public async Task<IReadOnlyList<Reminder>> ListPendingByAppointmentAsync(
        int appointmentId, CancellationToken cancellationToken) =>
        await context.Set<Reminder>()
            .Where(reminder => reminder.AppointmentId == appointmentId && reminder.Status == ReminderStatus.Pending)
            .OrderBy(reminder => reminder.ScheduledAt).ThenBy(reminder => reminder.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Reminder>> ListDueAsync(
        DateTimeOffset now, int limit, CancellationToken cancellationToken) =>
        await context.Set<Reminder>()
            .Where(reminder => reminder.Status == ReminderStatus.Pending && reminder.ScheduledAt <= now)
            .OrderBy(reminder => reminder.ScheduledAt).ThenBy(reminder => reminder.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
}
