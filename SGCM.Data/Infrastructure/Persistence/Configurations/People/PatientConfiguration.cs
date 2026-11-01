using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.People;

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patient", "people");

        builder.HasKey(patient => patient.Id);
        builder.Property(patient => patient.Id).UseIdentityAlwaysColumn();

        builder.Property(patient => patient.FirstName).HasMaxLength(100);
        builder.Property(patient => patient.LastName).HasMaxLength(100);
        builder.Property(patient => patient.PhoneNumber).HasMaxLength(20);
        builder.Property(patient => patient.Address).HasMaxLength(250);
        builder.Property(patient => patient.PolicyNumber).HasMaxLength(50);

        // Nombre y teléfono comparten la fila del paciente; si ambas columnas son NULL, no hay contacto.
        builder.OwnsOne(patient => patient.EmergencyContact, contact =>
        {
            contact.Property(value => value.Name)
                .HasColumnName("emergency_contact_name")
                .HasMaxLength(150);

            contact.Property(value => value.PhoneNumber)
                .HasColumnName("emergency_contact_phone")
                .HasMaxLength(20);
        });

        // El dominio siempre envía el estado, incluido false, sin delegarlo al DEFAULT SQL.
        builder.Property(patient => patient.IsActive).ValueGeneratedNever();

        builder.Ignore(patient => patient.DomainEvents);
    }
}
