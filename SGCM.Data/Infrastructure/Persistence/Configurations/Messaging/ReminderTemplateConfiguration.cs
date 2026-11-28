using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Messaging;

internal sealed class ReminderTemplateConfiguration : IEntityTypeConfiguration<ReminderTemplate>
{
    public void Configure(EntityTypeBuilder<ReminderTemplate> builder)
    {
        builder.ToTable("reminder_template", "messaging");

        builder.HasKey(template => template.Id);
        builder.Property(template => template.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Doctor>().WithMany().HasForeignKey(template => template.DoctorId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_reminder_template_doctor");

        builder.Property(template => template.Name).HasMaxLength(ReminderTemplate.NameMaxLength).IsRequired();
        builder.Property(template => template.Subject).HasMaxLength(ReminderTemplate.SubjectMaxLength).IsRequired();
        builder.Property(template => template.BodyTemplate)
            .HasMaxLength(ReminderTemplate.BodyTemplateMaxLength).IsRequired();
        // El dominio siempre envía el estado, incluido false, sin delegarlo al DEFAULT SQL.
        builder.Property(template => template.IsActive).IsRequired().ValueGeneratedNever();

        builder.Ignore(template => template.IsGlobal);
        builder.Ignore(template => template.DomainEvents);
    }
}
