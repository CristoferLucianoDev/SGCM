using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.People;

internal sealed class DoctorGeographyConfiguration : IEntityTypeConfiguration<ProvinceReference>,
    IEntityTypeConfiguration<MunicipalityReference>
{
    public void Configure(EntityTypeBuilder<ProvinceReference> builder)
    {
        builder.ToTable("province", "people");
        builder.HasKey(province => province.Id).HasName("pk_province");
        builder.Property(province => province.Id).UseIdentityAlwaysColumn();
    }

    public void Configure(EntityTypeBuilder<MunicipalityReference> builder)
    {
        builder.ToTable("municipality", "people");
        builder.HasKey(municipality => municipality.Id).HasName("pk_municipality");
        builder.Property(municipality => municipality.Id).UseIdentityAlwaysColumn();
        builder.HasAlternateKey(municipality => new { municipality.Id, municipality.ProvinceId })
            .HasName("uq_municipality_id_province_id");
        builder.HasOne<ProvinceReference>().WithMany().HasForeignKey(municipality => municipality.ProvinceId)
            .OnDelete(DeleteBehavior.NoAction).HasConstraintName("fk_municipality_province");
    }
}

internal sealed class ProvinceReference
{
    public int Id { get; private set; }
}

internal sealed class MunicipalityReference
{
    public int Id { get; private set; }
    public int ProvinceId { get; private set; }
}
