using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas de la rotación del refresh token y de la revocación por reutilización en <see cref="User.RefreshSession"/>.
/// </summary>
public class UserRefreshSessionTests
{
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";
    private const string FirstHash = "hash-primero";
    private const string SecondHash = "hash-segundo";
    private const string ThirdHash = "hash-tercero";
    private const string OtherHash = "hash-otra-sesion";
    private const string NewHash = "hash-nuevo";
    private const int MaxSessions = 5;

    private static readonly DateTimeOffset Now = UserMother.Now;
    private static readonly DateTimeOffset FirstRefreshAt = Now.AddHours(1);
    private static readonly DateTimeOffset SecondRefreshAt = Now.AddHours(2);
    private static readonly DateTimeOffset ThirdRefreshAt = Now.AddHours(3);
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private static readonly TimeSpan ReuseGracePeriod = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

    [Fact]
    public void RefreshSession_con_el_hash_vigente_rota_la_sesion()
    {
        var user = ConfirmedUser();
        var session = Start(user, FirstHash);

        var result = user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        result.Value.ShouldBeSameAs(session);
        session.RefreshTokenHash.ShouldBe(SecondHash);
        session.PreviousRefreshTokenHash.ShouldBe(FirstHash);
        session.ExpiresAt.ShouldBe(FirstRefreshAt + Lifetime);
        session.LastUsedAt.ShouldBe(FirstRefreshAt);
        session.IsActiveAt(FirstRefreshAt).ShouldBeTrue();
    }

    [Fact]
    public void RefreshSession_dos_veces_deja_como_anterior_solo_el_hash_inmediato()
    {
        var user = ConfirmedUser();
        var session = Start(user, FirstHash);

        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);
        user.RefreshSession(SecondHash, ThirdHash, SecondRefreshAt, Lifetime, ReuseGracePeriod);

        session.RefreshTokenHash.ShouldBe(ThirdHash);
        session.PreviousRefreshTokenHash.ShouldBe(SecondHash);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_revoca_todas_las_sesiones_activas()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        Start(user, OtherHash);
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        var result = user.RefreshSession(FirstHash, NewHash, SecondRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.RefreshTokenReused);
        user.Sessions.Count.ShouldBe(2);
        user.Sessions.ShouldAllBe(session =>
            session.RevokedAt == SecondRefreshAt && session.RevocationReason == SessionRevocationReason.TokenReuse);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_conserva_fecha_y_motivo_de_las_ya_revocadas()
    {
        var user = ConfirmedUser();
        var revoked = Start(user, OtherHash);
        var active = StartReplacingActive(user, FirstHash, Now.AddMinutes(1));
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        var result = user.RefreshSession(FirstHash, NewHash, SecondRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.RefreshTokenReused);
        revoked.RevokedAt.ShouldBe(Now.AddMinutes(1));
        revoked.RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
        active.RevokedAt.ShouldBe(SecondRefreshAt);
        active.RevocationReason.ShouldBe(SessionRevocationReason.TokenReuse);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_dentro_del_margen_devuelve_SessionInvalid_y_no_revoca()
    {
        var user = UserWithRotatedAndOtherSession();
        var before = Snapshot(user);

        var result = user.RefreshSession(
            FirstHash, NewHash, FirstRefreshAt + ReuseGracePeriod - OneSecond, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_en_el_limite_del_margen_devuelve_SessionInvalid_y_no_revoca()
    {
        var user = UserWithRotatedAndOtherSession();
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, FirstRefreshAt + ReuseGracePeriod, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_fuera_del_margen_revoca_todas_las_sesiones_activas()
    {
        var user = UserWithRotatedAndOtherSession();
        var outsideGrace = FirstRefreshAt + ReuseGracePeriod + OneSecond;

        var result = user.RefreshSession(FirstHash, NewHash, outsideGrace, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.RefreshTokenReused);
        user.Sessions.ShouldAllBe(session =>
            session.RevokedAt == outsideGrace && session.RevocationReason == SessionRevocationReason.TokenReuse);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_de_una_sesion_revocada_devuelve_SessionInvalid_y_no_revoca_otras()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);
        StartReplacingActive(user, OtherHash, SecondRefreshAt);
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, ThirdRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_de_una_sesion_vencida_devuelve_SessionInvalid_y_no_revoca_otras()
    {
        var user = ConfirmedUser();
        var expired = Start(user, FirstHash);
        var other = Start(user, OtherHash);
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);
        // La otra sesión se renueva después, así sigue activa cuando la primera ya venció.
        user.RefreshSession(OtherHash, ThirdHash, SecondRefreshAt, Lifetime, ReuseGracePeriod);
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, expired.ExpiresAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
        other.IsActiveAt(expired.ExpiresAt).ShouldBeTrue();
    }

    [Fact]
    public void RefreshSession_con_un_hash_rotado_dos_veces_atras_devuelve_SessionInvalid_y_no_revoca()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);
        user.RefreshSession(SecondHash, ThirdHash, SecondRefreshAt, Lifetime, ReuseGracePeriod);
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, ThirdRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    // Las sesiones nunca rotadas tienen el hash anterior nulo: un hash presentado nulo no debe tomarse por robo.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hash-inexistente")]
    public void RefreshSession_con_hash_inexistente_nulo_o_vacio_devuelve_SessionInvalid_sin_cambios(string? presentedHash)
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        var before = Snapshot(user);

        var result = user.RefreshSession(presentedHash!, NewHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_de_una_sesion_revocada_devuelve_SessionInvalid_sin_cambios()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        StartReplacingActive(user, OtherHash, Now.AddMinutes(1));
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_de_una_sesion_vencida_devuelve_SessionInvalid_sin_cambios()
    {
        var user = ConfirmedUser();
        var session = Start(user, FirstHash);
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, session.ExpiresAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_de_una_cuenta_inactiva_devuelve_SessionInvalid_sin_cambios()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        user.Deactivate();
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_con_el_hash_anterior_en_una_cuenta_inactiva_devuelve_SessionInvalid_y_no_revoca()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);
        user.Deactivate();
        var before = Snapshot(user);

        var result = user.RefreshSession(FirstHash, NewHash, SecondRefreshAt, Lifetime, ReuseGracePeriod);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        Snapshot(user).ShouldBe(before);
    }

    [Fact]
    public void RefreshSession_no_afecta_a_las_demas_sesiones_del_usuario()
    {
        var user = ConfirmedUser();
        var other = Start(user, OtherHash);
        Start(user, FirstHash);
        var otherBefore = SessionState.From(other);

        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);

        SessionState.From(other).ShouldBe(otherBefore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RefreshSession_sin_hash_nuevo_lanza_DomainException(string? newHash)
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);

        Should.Throw<DomainException>(() => user.RefreshSession(FirstHash, newHash!, FirstRefreshAt, Lifetime, ReuseGracePeriod));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RefreshSession_con_vigencia_no_positiva_lanza_DomainException(int lifetimeDays)
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);

        Should.Throw<DomainException>(() =>
            user.RefreshSession(FirstHash, NewHash, FirstRefreshAt, TimeSpan.FromDays(lifetimeDays), ReuseGracePeriod));
    }

    [Fact]
    public void RefreshSession_con_margen_negativo_lanza_DomainException()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);

        Should.Throw<DomainException>(() =>
            user.RefreshSession(FirstHash, NewHash, FirstRefreshAt, Lifetime, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void RefreshSession_con_hash_nuevo_igual_al_presentado_lanza_DomainException()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);

        Should.Throw<DomainException>(() =>
            user.RefreshSession(FirstHash, FirstHash, FirstRefreshAt, Lifetime, ReuseGracePeriod));
    }

    [Fact]
    public void RefreshTokenReused_y_SessionInvalid_tienen_la_misma_descripcion_y_distinto_codigo()
    {
        UserErrors.RefreshTokenReused.Description.ShouldBe(UserErrors.SessionInvalid.Description);
        UserErrors.RefreshTokenReused.Code.ShouldNotBe(UserErrors.SessionInvalid.Code);
    }

    private static User ConfirmedUser()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, Now);
        return user;
    }

    private static User UserWithRotatedAndOtherSession()
    {
        var user = ConfirmedUser();
        Start(user, FirstHash);
        Start(user, OtherHash);
        user.RefreshSession(FirstHash, SecondHash, FirstRefreshAt, Lifetime, ReuseGracePeriod);
        return user;
    }

    private static UserSession Start(User user, string refreshTokenHash) =>
        user.StartSession(refreshTokenHash, DeviceName, IpAddress, Now, Lifetime, MaxSessions).Value;

    // Con un máximo de una sesión, abrir otra revoca la activa por SessionLimit usando solo la API pública.
    private static UserSession StartReplacingActive(User user, string refreshTokenHash, DateTimeOffset at) =>
        user.StartSession(refreshTokenHash, DeviceName, IpAddress, at, Lifetime, maxActiveSessions: 1).Value;

    private static List<SessionState> Snapshot(User user) =>
        user.Sessions.Select(SessionState.From).ToList();

    private sealed record SessionState(
        string RefreshTokenHash,
        string? PreviousRefreshTokenHash,
        DateTimeOffset ExpiresAt,
        DateTimeOffset LastUsedAt,
        DateTimeOffset? RevokedAt,
        SessionRevocationReason? RevocationReason)
    {
        public static SessionState From(UserSession session) =>
            new(
                session.RefreshTokenHash,
                session.PreviousRefreshTokenHash,
                session.ExpiresAt,
                session.LastUsedAt,
                session.RevokedAt,
                session.RevocationReason);
    }
}
