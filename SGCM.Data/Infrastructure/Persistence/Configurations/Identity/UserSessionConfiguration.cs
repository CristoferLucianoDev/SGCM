using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Configurations.Identity;

internal sealed class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    private const int TokenHashMaxLength = 256;
    private const int RevocationReasonMaxLength = 50;

    // Mapeo explícito con los textos del check ck_user_session_revocation_reason: si se
    // renombra un valor del enum, la conversión falla en vez de escribir un texto que el
    // check rechace.
    private static readonly ValueConverter<SessionRevocationReason, string> RevocationReasonConverter =
        new(reason => ToDatabase(reason), value => FromDatabase(value));

    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("user_session", "app");

        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).UseIdentityAlwaysColumn();

        builder.Property(session => session.RefreshTokenHash).HasMaxLength(TokenHashMaxLength);
        builder.Property(session => session.PreviousRefreshTokenHash).HasMaxLength(TokenHashMaxLength);
        builder.Property(session => session.DeviceName).HasMaxLength(UserSession.MaxDeviceNameLength);
        builder.Property(session => session.IpAddress).HasMaxLength(UserSession.MaxIpAddressLength);

        builder.Property(session => session.RevocationReason)
            .HasConversion(RevocationReasonConverter)
            .HasMaxLength(RevocationReasonMaxLength);

        // created_at no se mapea porque el dominio no lo usa; lo llena la base.
    }

    private static string ToDatabase(SessionRevocationReason reason) => reason switch
    {
        SessionRevocationReason.Logout => "logout",
        SessionRevocationReason.PasswordReset => "password_reset",
        SessionRevocationReason.TwoFactorDisabled => "two_factor_disabled",
        SessionRevocationReason.AccountDeactivated => "account_deactivated",
        SessionRevocationReason.TokenReuse => "token_reuse",
        SessionRevocationReason.SessionLimit => "session_limit",
        _ => throw new InvalidOperationException($"El motivo de revocación {reason} no tiene texto en la base.")
    };

    private static SessionRevocationReason FromDatabase(string value) => value switch
    {
        "logout" => SessionRevocationReason.Logout,
        "password_reset" => SessionRevocationReason.PasswordReset,
        "two_factor_disabled" => SessionRevocationReason.TwoFactorDisabled,
        "account_deactivated" => SessionRevocationReason.AccountDeactivated,
        "token_reuse" => SessionRevocationReason.TokenReuse,
        "session_limit" => SessionRevocationReason.SessionLimit,
        _ => throw new InvalidOperationException($"El motivo de revocación '{value}' de la base no es conocido.")
    };
}
