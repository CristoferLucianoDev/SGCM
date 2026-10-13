using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("user", "app");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).UseIdentityAlwaysColumn();

        builder.Property(user => user.Email)
            .HasConversion(email => email.Value, value => Email.Create(value).Value)
            .HasMaxLength(Email.MaxLength);

        builder.Property(user => user.PasswordHash).HasMaxLength(512);

        builder.Property(user => user.Role)
            .HasColumnName("role_id")
            .HasConversion<int>();

        builder.HasMany(user => user.Tokens)
            .WithOne()
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(user => user.Tokens).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(user => user.Sessions)
            .WithOne()
            .HasForeignKey("UserId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(user => user.Sessions).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(user => user.DomainEvents);
    }
}