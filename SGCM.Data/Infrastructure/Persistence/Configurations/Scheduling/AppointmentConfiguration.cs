using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Scheduling;

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    // ck_appointment_status guarda el estado en minúsculas: pending, confirmed, rejected, cancelled, completed.
    private static readonly ValueConverter<AppointmentStatus, string> StatusConverter = new(
        status => status.ToString().ToLowerInvariant(),
        value => Enum.Parse<AppointmentStatus>(value, true));

    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointment", "scheduling");

        builder.HasKey(appointment => appointment.Id);
        builder.Property(appointment => appointment.Id).UseIdentityAlwaysColumn();

        builder.HasOne<Patient>().WithMany().HasForeignKey(appointment => appointment.PatientId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_appointment_patient");
        builder.HasOne<Doctor>().WithMany().HasForeignKey(appointment => appointment.DoctorId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_appointment_doctor");
        builder.HasOne<Specialty>().WithMany().HasForeignKey(appointment => appointment.SpecialtyId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_appointment_specialty");

        // El dominio siempre envía el estado, sin delegarlo al DEFAULT SQL.
        builder.Property(appointment => appointment.Status)
            .HasConversion(StatusConverter).HasMaxLength(20).IsRequired().ValueGeneratedNever();
        builder.Property(appointment => appointment.ReasonForVisit).HasMaxLength(Appointment.ReasonForVisitMaxLength);
        builder.Property(appointment => appointment.StatusReason).HasMaxLength(500);

        builder.Ignore(appointment => appointment.DomainEvents);
    }
}
