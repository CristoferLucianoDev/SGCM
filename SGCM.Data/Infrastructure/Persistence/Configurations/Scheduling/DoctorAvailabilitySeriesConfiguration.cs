using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Scheduling;

internal sealed class DoctorAvailabilitySeriesConfiguration : IEntityTypeConfiguration<DoctorAvailabilitySeries>
{
    public const int MaxTimeZoneLength = 50;

    internal const string DaysOfWeekField = "_isoDaysOfWeek";

    public void Configure(EntityTypeBuilder<DoctorAvailabilitySeries> builder)
    {
        builder.ToTable("doctor_availability_series", "scheduling");

        builder.HasKey(series => series.Id);
        builder.Property(series => series.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Doctor>().WithMany().HasForeignKey(series => series.DoctorId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_doctor_availability_series_doctor");

        // El dominio expone los días como DayOfWeek y los guarda en su campo como arreglo ISO 8601
        // (1 = lunes ... 7 = domingo), igual que la columna; Npgsql lo envía tal cual como smallint[].
        builder.Ignore(series => series.DaysOfWeek);
        builder.Ignore(series => series.Recurrence);
        builder.Property<short[]>(DaysOfWeekField)
            .HasColumnName("days_of_week")
            .HasColumnType("smallint[]")
            .IsRequired();

        builder.Property(series => series.TimeZone)
            .HasMaxLength(MaxTimeZoneLength)
            .IsRequired();

        // El dominio siempre envía el estado, incluido false, sin delegarlo al DEFAULT SQL.
        builder.Property(series => series.IsActive).IsRequired().ValueGeneratedNever();

        builder.Ignore(series => series.DomainEvents);
    }
}
