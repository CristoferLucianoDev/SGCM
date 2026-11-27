using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Clinical;

internal sealed class MedicalRecordAccessLogConfiguration : IEntityTypeConfiguration<MedicalRecordAccessLog>
{
    private const int AccessTypeMaxLength = 20;

    // El CHECK de la tabla guarda el tipo en minúsculas: view, create, update.
    private static readonly ValueConverter<MedicalRecordAccessType, string> AccessTypeConverter = new(
        type => type.ToString().ToLowerInvariant(),
        value => Enum.Parse<MedicalRecordAccessType>(value, true));

    public void Configure(EntityTypeBuilder<MedicalRecordAccessLog> builder)
    {
        builder.ToTable("medical_record_access_log", "clinical");

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).UseIdentityAlwaysColumn();

        builder.HasOne<MedicalRecord>().WithMany().HasForeignKey(log => log.MedicalRecordId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_medical_record_access_log_medical_record");
        builder.HasOne<User>().WithMany().HasForeignKey(log => log.AccessedBy)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_medical_record_access_log_user");

        builder.Property(log => log.AccessType)
            .HasConversion(AccessTypeConverter).HasMaxLength(AccessTypeMaxLength).IsRequired();
        // El dominio envía la hora del reloj de la aplicación, sin delegarla al DEFAULT now() de la tabla.
        builder.Property(log => log.AccessedAt).IsRequired().ValueGeneratedNever();
        builder.Property(log => log.IpAddress).HasMaxLength(MedicalRecordAccessLog.IpAddressMaxLength);
        // created_*, updated_* no se mapean: lo llena la base y una fila nunca se actualiza.

        builder.Ignore(log => log.DomainEvents);
    }
}
