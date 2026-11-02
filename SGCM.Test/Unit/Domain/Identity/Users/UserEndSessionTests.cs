using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del cierre de sesión en <see cref="User.EndSession"/>.
/// </summary>
public class UserEndSessionTests
{
    private const string SessionHash = "hash-sesion";
    private const string OtherSessionHash = "hash-otra-sesion";
    private const string RotatedHash = "hash-rotado";
    private const string AttackerHash = "hash-del-atacante";
    private const int MaxSessions = 5;

    private static readonly DateTimeOffset Now = UserMother.Now;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromSeconds(30);

    [Fact]
    public void EndSession_revoca_con_Logout_solo_la_sesion_del_hash()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);
        var other = StartSession(user, OtherSessionHash);
        var endedAt = Now.AddMinutes(5);

        var outcome = user.EndSession(SessionHash, endedAt);

        outcome.ShouldBe(SessionEndOutcome.ClosedByCurrentToken);
        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        session.RevokedAt.ShouldBe(endedAt);
        other.RevokedAt.ShouldBeNull();
        other.RevocationReason.ShouldBeNull();
    }

    [Theory]
    [InlineData("hash-inexistente")]
    [InlineData("")]
    [InlineData(null)]
    public void EndSession_con_hash_que_no_coincide_no_cambia_nada(string? refreshTokenHash)
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);

        var outcome = user.EndSession(refreshTokenHash!, Now);

        outcome.ShouldBe(SessionEndOutcome.None);
        session.RevokedAt.ShouldBeNull();
        session.RevocationReason.ShouldBeNull();
    }

    [Fact]
    public void EndSession_de_una_sesion_ya_revocada_conserva_fecha_y_motivo_originales()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);
        var firstEndedAt = Now.AddMinutes(1);
        user.EndSession(SessionHash, firstEndedAt);

        var outcome = user.EndSession(SessionHash, Now.AddMinutes(2));

        outcome.ShouldBe(SessionEndOutcome.None);
        session.RevokedAt.ShouldBe(firstEndedAt);
        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
    }

    [Fact]
    public void EndSession_de_una_sesion_revocada_por_otro_motivo_conserva_ese_motivo()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);
        var revokedAt = session.ExpiresAt.AddDays(-1);
        user.RefreshSession(SessionHash, RotatedHash, Now, Lifetime, GracePeriod);
        user.RefreshSession(SessionHash, AttackerHash, revokedAt, Lifetime, GracePeriod);

        var outcome = user.EndSession(RotatedHash, revokedAt.AddMinutes(1));

        outcome.ShouldBe(SessionEndOutcome.None);
        session.RevokedAt.ShouldBe(revokedAt);
        session.RevocationReason.ShouldBe(SessionRevocationReason.TokenReuse);
    }

    [Fact]
    public void EndSession_de_una_sesion_vencida_no_la_modifica()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);

        var outcome = user.EndSession(SessionHash, session.ExpiresAt);

        outcome.ShouldBe(SessionEndOutcome.None);
        session.RevokedAt.ShouldBeNull();
        session.RevocationReason.ShouldBeNull();
    }

    [Fact]
    public void EndSession_con_el_hash_anterior_de_una_sesion_activa_la_cierra_con_Logout_y_no_toca_las_demas()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);
        var other = StartSession(user, OtherSessionHash);
        user.RefreshSession(SessionHash, RotatedHash, Now, Lifetime, GracePeriod);
        var endedAt = Now.AddMinutes(1);

        var outcome = user.EndSession(SessionHash, endedAt);

        outcome.ShouldBe(SessionEndOutcome.ClosedByPreviousToken);
        session.RevokedAt.ShouldBe(endedAt);
        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        other.RevokedAt.ShouldBeNull();
        other.RevocationReason.ShouldBeNull();
    }

    [Fact]
    public void EndSession_con_el_hash_anterior_de_una_sesion_ya_revocada_conserva_fecha_y_motivo()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);
        user.RefreshSession(SessionHash, RotatedHash, Now, Lifetime, GracePeriod);
        var firstEndedAt = Now.AddMinutes(1);
        var closedWithCurrent = user.EndSession(RotatedHash, firstEndedAt);

        var outcome = user.EndSession(SessionHash, Now.AddMinutes(2));

        closedWithCurrent.ShouldBe(SessionEndOutcome.ClosedByCurrentToken);
        outcome.ShouldBe(SessionEndOutcome.None);
        session.RevokedAt.ShouldBe(firstEndedAt);
        session.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
    }

    [Fact]
    public void EndSession_con_el_hash_anterior_de_una_sesion_vencida_no_la_modifica()
    {
        var user = ConfirmedUser();
        var session = StartSession(user, SessionHash);
        user.RefreshSession(SessionHash, RotatedHash, Now, Lifetime, GracePeriod);

        var outcome = user.EndSession(SessionHash, session.ExpiresAt);

        outcome.ShouldBe(SessionEndOutcome.None);
        session.RevokedAt.ShouldBeNull();
        session.RevocationReason.ShouldBeNull();
    }

    [Fact]
    public void EndSession_con_un_hash_anterior_repetido_cierra_la_sesion_activa_aunque_haya_una_revocada_antes()
    {
        var user = ConfirmedUser();
        var revoked = StartSession(user, SessionHash);
        user.RefreshSession(SessionHash, RotatedHash, Now, Lifetime, GracePeriod);
        user.EndSession(RotatedHash, Now);
        var active = StartSession(user, SessionHash);
        user.RefreshSession(SessionHash, OtherSessionHash, Now, Lifetime, GracePeriod);
        var endedAt = Now.AddMinutes(1);

        var outcome = user.EndSession(SessionHash, endedAt);

        outcome.ShouldBe(SessionEndOutcome.ClosedByPreviousToken);
        revoked.RevokedAt.ShouldBe(Now);
        active.RevokedAt.ShouldBe(endedAt);
        active.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
    }

    private static UserSession StartSession(User user, string refreshTokenHash) =>
        user.StartSession(refreshTokenHash, null, null, Now, Lifetime, MaxSessions).Value;

    private static User ConfirmedUser()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, Now);
        return user;
    }
}
