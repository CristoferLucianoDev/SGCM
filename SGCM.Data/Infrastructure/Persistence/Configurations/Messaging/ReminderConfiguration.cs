using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Messaging;

internal sealed class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    // ck_reminder_delivery_status guarda el estado en minúsculas: pending, sent, failed, cancelled.
    private static readonly ValueConverter<ReminderStatus, string> StatusConverter = new(
        status => status.ToString().ToLowerInvariant(),
        value => Enum.Parse<ReminderStatus>(value, true));

    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("reminder", "messaging");

        builder.HasKey(reminder => reminder.Id);
        builder.Property(reminder => reminder.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Appointment>().WithMany().HasForeignKey(reminder => reminder.AppointmentId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_reminder_appointment");
        builder.HasOne<ReminderTemplate>().WithMany().HasForeignKey(reminder => reminder.TemplateId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_reminder_reminder_template");
        builder.HasOne<User>().WithMany().HasForeignKey(reminder => reminder.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_reminder_created_by");

        builder.Property(reminder => reminder.MessageBody).HasMaxLength(Reminder.MessageMaxLength).IsRequired();
        builder.Property(reminder => reminder.ScheduledAt).IsRequired();
        // El dominio siempre envía el estado, sin delegarlo al DEFAULT SQL.
        builder.Property(reminder => reminder.Status).HasColumnName("delivery_status")
            .HasConversion(StatusConverter).HasMaxLength(20).IsRequired().ValueGeneratedNever();
        // created_at lo pone la base; updated_at lo pone el trigger. updated_by no se mapea.

        builder.Ignore(reminder => reminder.DomainEvents);
    }
}
