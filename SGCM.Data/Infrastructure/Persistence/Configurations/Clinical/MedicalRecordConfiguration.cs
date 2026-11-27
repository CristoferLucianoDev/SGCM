using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Clinical;

internal sealed class MedicalRecordConfiguration : IEntityTypeConfiguration<MedicalRecord>
{
    public void Configure(EntityTypeBuilder<MedicalRecord> builder)
    {
        builder.ToTable("medical_record", "clinical");

        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).UseIdentityAlwaysColumn();

        // La clave compuesta hacia la cita garantiza que el paciente del registro sea el paciente de la cita.
        builder.HasOne<Appointment>().WithMany()
            .HasForeignKey(record => new { record.AppointmentId, record.PatientId })
            .HasPrincipalKey(appointment => new { appointment.Id, appointment.PatientId })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_medical_record_appointment");
        builder.HasOne<Patient>().WithMany().HasForeignKey(record => record.PatientId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_medical_record_patient");
        builder.HasOne<User>().WithMany().HasForeignKey(record => record.CreatedBy)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_medical_record_created_by");

        builder.Property(record => record.Diagnosis).HasMaxLength(MedicalRecordContent.DiagnosisMaxLength);
        builder.Property(record => record.Treatment).HasMaxLength(MedicalRecordContent.TreatmentMaxLength);
        builder.Property(record => record.Notes).HasMaxLength(MedicalRecordContent.NotesMaxLength);
        // El dominio envía la hora del reloj de la aplicación, sin delegarla al DEFAULT now() de la tabla: con ella
        // se decide cuál versión es la vigente.
        builder.Property(record => record.CreatedAt).IsRequired().ValueGeneratedNever();
        builder.Property(record => record.CreatedBy).IsRequired();
        // updated_at y updated_by no se mapean: una versión nunca se actualiza.

        builder.Ignore(record => record.DomainEvents);
    }
}
