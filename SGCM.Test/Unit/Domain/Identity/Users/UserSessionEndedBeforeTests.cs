using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas de <see cref="UserSession.EndedBefore"/>, la regla que decide qué sesiones borra el
/// proceso de retención.
/// </summary>
public class UserSessionEndedBeforeTests
{
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";

    private static readonly DateTimeOffset Now = UserMother.Now;

    // Vigencia de la sesión y máximo de sesiones activas que fija la arquitectura.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private const int MaxSessions = 5;

    // Con este máximo, abrir una segunda sesión revoca la primera por límite de sesiones.
    private const int SingleSession = 1;

    [Fact]
    public void EndedBefore_de_una_sesion_activa_devuelve_false()
    {
        var session = Start(ConfirmedUser(), Now, "hash-sesion", MaxSessions);

        session.EndedBefore(Now.AddMinutes(1)).ShouldBeFalse();
    }

    [Fact]
    public void EndedBefore_de_una_sesion_vencida_antes_del_corte_devuelve_true()
    {
        var session = Start(ConfirmedUser(), Now, "hash-sesion", MaxSessions);

        session.EndedBefore(session.ExpiresAt.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void EndedBefore_con_el_vencimiento_igual_al_corte_devuelve_false()
    {
        var session = Start(ConfirmedUser(), Now, "hash-sesion", MaxSessions);

        session.EndedBefore(session.ExpiresAt).ShouldBeFalse();
    }

    [Fact]
    public void EndedBefore_de_una_sesion_revocada_antes_del_corte_devuelve_true()
    {
        var revokedAt = Now.AddMinutes(1);
        var session = StartRevokedSession(revokedAt);

        session.EndedBefore(revokedAt.AddMinutes(1)).ShouldBeTrue();
    }

    [Fact]
    public void EndedBefore_de_una_sesion_revocada_despues_del_corte_y_no_vencida_devuelve_false()
    {
        var revokedAt = Now.AddMinutes(1);
        var session = StartRevokedSession(revokedAt);

        session.EndedBefore(revokedAt.AddMinutes(-1)).ShouldBeFalse();
    }

    private static User ConfirmedUser()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, Now);
        return user;
    }

    // Abre una sesión en Now y la revoca por límite al abrir otra en revokedAt.
    private static UserSession StartRevokedSession(DateTimeOffset revokedAt)
    {
        var user = ConfirmedUser();
        var session = Start(user, Now, "hash-primera", MaxSessions);
        Start(user, revokedAt, "hash-segunda", SingleSession);

        session.RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
        session.RevokedAt.ShouldBe(revokedAt);
        return session;
    }

    private static UserSession Start(User user, DateTimeOffset at, string refreshTokenHash, int maxActiveSessions) =>
        user.StartSession(refreshTokenHash, DeviceName, IpAddress, at, Lifetime, maxActiveSessions).Value;
}
