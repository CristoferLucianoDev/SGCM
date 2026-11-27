using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Scheduling;

internal sealed class AppointmentReportLogConfiguration : IEntityTypeConfiguration<AppointmentReportLog>
{
    private const int ActionMaxLength = 20;
    private const int ReportTypeMaxLength = 20;

    // Los CHECK de la tabla guardan la acción y el tipo en minúsculas: generate, export; completed, cancelled,
    // pending, all.
    private static readonly ValueConverter<ReportAction, string> ActionConverter = new(
        action => action.ToString().ToLowerInvariant(),
        value => Enum.Parse<ReportAction>(value, true));

    private static readonly ValueConverter<ReportType, string> ReportTypeConverter = new(
        type => type.ToString().ToLowerInvariant(),
        value => Enum.Parse<ReportType>(value, true));

    public void Configure(EntityTypeBuilder<AppointmentReportLog> builder)
    {
        builder.ToTable("appointment_report_log", "scheduling");

        builder.HasKey(log => log.Id);
        builder.Property(log => log.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Doctor>().WithMany().HasForeignKey(log => log.DoctorId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_appointment_report_log_doctor");

        builder.Property(log => log.Action)
            .HasConversion(ActionConverter).HasMaxLength(ActionMaxLength).IsRequired();
        builder.Property(log => log.ReportType)
            .HasConversion(ReportTypeConverter).HasMaxLength(ReportTypeMaxLength).IsRequired();
        builder.Property(log => log.Filters).HasColumnType("jsonb").IsRequired().ValueGeneratedNever();
        builder.Property(log => log.ExportFormat).HasMaxLength(AppointmentReportLog.ExportFormatMaxLength);
        builder.Property(log => log.RowCount).IsRequired();
        // El dominio envía la hora del reloj de la aplicación, sin delegarla al DEFAULT now() de la tabla.
        builder.Property(log => log.GeneratedAt).IsRequired().ValueGeneratedNever();

        builder.Ignore(log => log.DomainEvents);
    }
}
