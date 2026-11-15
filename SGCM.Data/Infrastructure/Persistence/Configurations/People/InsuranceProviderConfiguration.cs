using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.People.InsuranceProviders;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.People;

internal sealed class InsuranceProviderConfiguration : IEntityTypeConfiguration<InsuranceProvider>
{
    public void Configure(EntityTypeBuilder<InsuranceProvider> builder)
    {
        builder.ToTable("insurance_provider", "people");

        builder.HasKey(provider => provider.Id);
        builder.Property(provider => provider.Id).UseIdentityAlwaysColumn();

        builder.Property(provider => provider.Name)
            .HasMaxLength(InsuranceProvider.MaxNameLength)
            .IsRequired();

        builder.Property(provider => provider.ContactPhone)
            .HasMaxLength(InsuranceProvider.MaxContactPhoneLength)
            .IsRequired();

        builder.Property(provider => provider.ContactEmail)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasMaxLength(Email.MaxLength)
            .IsRequired();

        builder.Property(provider => provider.Address)
            .HasMaxLength(InsuranceProvider.MaxAddressLength)
            .IsRequired();

        builder.Property(provider => provider.Website)
            .HasMaxLength(InsuranceProvider.MaxWebsiteLength);

        // El dominio siempre envía el estado, incluido false, sin delegarlo al DEFAULT SQL.
        builder.Property(provider => provider.IsActive).IsRequired().ValueGeneratedNever();

        builder.Ignore(provider => provider.DomainEvents);
    }
}
