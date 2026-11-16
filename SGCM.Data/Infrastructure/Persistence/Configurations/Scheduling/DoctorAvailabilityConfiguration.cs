using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Scheduling;

internal sealed class DoctorAvailabilityConfiguration : IEntityTypeConfiguration<DoctorAvailability>
{
    public void Configure(EntityTypeBuilder<DoctorAvailability> builder)
    {
        builder.ToTable("doctor_availability", "scheduling");

        builder.HasKey(block => block.Id);
        builder.Property(block => block.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Doctor>().WithMany().HasForeignKey(block => block.DoctorId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_doctor_availability_doctor");

        // La base además exige que la serie sea del mismo médico con la FK compuesta
        // fk_doctor_availability_doctor_availability_series (series_id, doctor_id).
        builder.Property(block => block.SeriesId);

        // El dominio siempre envía los indicadores, incluido false, sin delegarlos al DEFAULT SQL.
        builder.Property(block => block.IsAvailable).IsRequired().ValueGeneratedNever();
        builder.Property(block => block.IsModified).IsRequired().ValueGeneratedNever();

        builder.Ignore(block => block.DomainEvents);
    }
}
