using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.People;

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctor", "people");
        builder.HasKey(doctor => doctor.Id);
        builder.Property(doctor => doctor.Id).UseIdentityAlwaysColumn();
        builder.Property(doctor => doctor.FirstName).HasMaxLength(Doctor.MaxFirstNameLength).IsRequired();
        builder.Property(doctor => doctor.LastName).HasMaxLength(Doctor.MaxLastNameLength).IsRequired();
        builder.Property(doctor => doctor.LicenseNumber).HasMaxLength(Doctor.MaxLicenseNumberLength).IsRequired();
        builder.Property(doctor => doctor.PhoneNumber)
            .HasConversion(phone => phone.Value, value => PhoneNumber.FromStoredValue(value))
            .HasMaxLength(Doctor.MaxPhoneNumberLength).IsRequired();
        builder.Property(doctor => doctor.AddressLine).HasMaxLength(Doctor.MaxAddressLineLength).IsRequired();
        builder.Property(doctor => doctor.Biography).HasMaxLength(Doctor.MaxBiographyLength).IsRequired();
        builder.Property(doctor => doctor.ConsultationFee)
            .HasConversion(fee => fee.Value, value => Money.FromStoredValue(value))
            .HasPrecision(10, 2);
        builder.Property(doctor => doctor.IsActive).IsRequired().ValueGeneratedNever();
        builder.HasIndex(doctor => doctor.UserId).IsUnique().HasDatabaseName("uq_doctor_user_id");
        builder.HasIndex(doctor => doctor.LicenseNumber).IsUnique().HasDatabaseName("uq_doctor_license_number");
        builder.HasOne<User>().WithOne().HasForeignKey<Doctor>(doctor => doctor.UserId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_doctor_user");
        builder.HasOne<ProvinceReference>().WithMany().HasForeignKey(doctor => doctor.ProvinceId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_doctor_province");
        builder.HasOne<MunicipalityReference>().WithMany()
            .HasForeignKey(doctor => new { doctor.MunicipalityId, doctor.ProvinceId })
            .HasPrincipalKey(municipality => new { municipality.Id, municipality.ProvinceId })
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_doctor_municipality");
        builder.Ignore(doctor => doctor.DomainEvents);

        builder.OwnsMany(doctor => doctor.Specialties, links =>
        {
            links.ToTable("doctor_specialty", "people");
            links.WithOwner().HasForeignKey("DoctorId").HasConstraintName("fk_doctor_specialty_doctor");
            links.Property<int>("DoctorId").HasColumnName("doctor_id");
            links.Property(link => link.SpecialtyId).HasColumnName("specialty_id").ValueGeneratedNever();
            links.HasKey("DoctorId", nameof(DoctorSpecialty.SpecialtyId)).HasName("pk_doctor_specialty");
            links.HasOne<Specialty>().WithMany().HasForeignKey(link => link.SpecialtyId)
                .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_doctor_specialty_specialty");
        });
        builder.Navigation(doctor => doctor.Specialties)
            .HasField("_specialties").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
