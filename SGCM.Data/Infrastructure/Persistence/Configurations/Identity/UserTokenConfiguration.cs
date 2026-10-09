using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserTokenConfiguration : IEntityTypeConfiguration<UserToken>
{
    public void Configure(EntityTypeBuilder<UserToken> builder)
    {
        builder.ToTable("user_token", "app");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).UseIdentityAlwaysColumn();

        builder.Property(token => token.Type)
            .HasColumnName("token_type_id")
            .HasConversion<int>();

        builder.Property(token => token.TokenHash).HasMaxLength(256);

        // La tabla no tiene issued_at: la emisión es la creación del registro.
        builder.Property(token => token.IssuedAt).HasColumnName("created_at");
    }
}