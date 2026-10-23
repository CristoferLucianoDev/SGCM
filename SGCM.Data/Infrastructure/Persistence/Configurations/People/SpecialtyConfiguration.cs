using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.People;

internal sealed class SpecialtyConfiguration : IEntityTypeConfiguration<Specialty>
{
    public void Configure(EntityTypeBuilder<Specialty> builder)
    {
        builder.ToTable("specialty", "people");

        builder.HasKey(specialty => specialty.Id);
        builder.Property(specialty => specialty.Id).UseIdentityAlwaysColumn();

        builder.Property(specialty => specialty.Name)
            .HasMaxLength(Specialty.MaxNameLength)
            .IsRequired();

        builder.Property(specialty => specialty.Description)
            .HasMaxLength(Specialty.MaxDescriptionLength)
            .IsRequired();

        // El dominio siempre envía el estado, incluido false, sin delegarlo al DEFAULT SQL.
        builder.Property(specialty => specialty.IsActive).IsRequired().ValueGeneratedNever();

        builder.Ignore(specialty => specialty.DomainEvents);
    }
}
