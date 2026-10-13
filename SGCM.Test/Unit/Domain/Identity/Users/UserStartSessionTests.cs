using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del inicio de sesión en <see cref="User.StartSession"/> y del límite de sesiones activas.
/// </summary>
public class UserStartSessionTests
{
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";

    private static readonly DateTimeOffset Now = UserMother.Now;

    // Vigencia de la sesión y máximo de sesiones activas que fija la arquitectura.
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
    private const int MaxSessions = 5;

    [Fact]
    public void StartSession_crea_una_sesion_activa_con_sus_datos()
    {
        var user = ConfirmedUser();

        var session = user.StartSession("hash-sesion", DeviceName, IpAddress, Now, Lifetime, MaxSessions).Value;

        session.RefreshTokenHash.ShouldBe("hash-sesion");
        session.DeviceName.ShouldBe(DeviceName);
        session.IpAddress.ShouldBe(IpAddress);
        session.ExpiresAt.ShouldBe(Now + Lifetime);
        session.LastUsedAt.ShouldBe(Now);
        session.IsActiveAt(Now).ShouldBeTrue();
        session.RevokedAt.ShouldBeNull();
        session.RevocationReason.ShouldBeNull();
        user.Sessions.ShouldHaveSingleItem().ShouldBeSameAs(session);
    }

    [Fact]
    public void StartSession_con_menos_del_maximo_no_revoca_ninguna()
    {
        var user = ConfirmedUser();

        StartSessions(user, MaxSessions);

        user.Sessions.Count.ShouldBe(MaxSessions);
        user.Sessions.ShouldAllBe(session => session.RevokedAt == null);
    }

    [Fact]
    public void StartSession_al_superar_el_maximo_revoca_la_usada_hace_mas_tiempo()
    {
        var user = ConfirmedUser();
        var sessions = StartSessions(user, MaxSessions);
        var sixthAt = Now.AddMinutes(MaxSessions);

        var sixth = user.StartSession("hash-sexta", DeviceName, IpAddress, sixthAt, Lifetime, MaxSessions).Value;

        var first = sessions[0];
        first.RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
        first.RevokedAt.ShouldBe(sixthAt);
        var active = user.Sessions.Where(session => session.IsActiveAt(sixthAt)).ToList();
        active.Count.ShouldBe(MaxSessions);
        active.ShouldContain(sixth);
    }

    [Fact]
    public void StartSession_no_cuenta_las_sesiones_revocadas_ni_vencidas()
    {
        var user = ConfirmedUser();
        StartSessions(user, MaxSessions);
        // Las sesiones se abrieron con un minuto de diferencia: en este momento vencieron las tres primeras.
        var at = Now + Lifetime + TimeSpan.FromMinutes(2);

        user.StartSession("hash-nueva", DeviceName, IpAddress, at, Lifetime, MaxSessions);

        user.Sessions.ShouldAllBe(session => session.RevokedAt == null);
        user.Sessions.Count(session => session.IsActiveAt(at)).ShouldBe(3);
    }

    [Fact]
    public void StartSession_con_mas_sesiones_activas_que_el_maximo_revoca_las_sobrantes()
    {
        const int reducedMax = 3;
        var user = ConfirmedUser();
        var sessions = StartSessions(user, MaxSessions);
        var at = Now.AddMinutes(10);

        user.StartSession("hash-nueva", DeviceName, IpAddress, at, Lifetime, reducedMax);

        user.Sessions.Count(session => session.IsActiveAt(at)).ShouldBe(reducedMax);
        user.Sessions
            .Where(session => session.RevocationReason == SessionRevocationReason.SessionLimit)
            .ShouldBe(sessions.Take(MaxSessions + 1 - reducedMax), ignoreOrder: true);
    }

    [Fact]
    public void StartSession_de_una_cuenta_inactiva_se_rechaza()
    {
        var user = ConfirmedUser();
        user.Deactivate();

        var result = Start(user, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.AccountInactive);
        user.Sessions.ShouldBeEmpty();
    }

    [Fact]
    public void StartSession_de_una_cuenta_sin_confirmar_se_rechaza()
    {
        var user = UserMother.Pending();

        var result = Start(user, Now.AddMinutes(1));

        result.Error.ShouldBe(UserErrors.EmailNotConfirmed);
        user.Sessions.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("", 10, 10, 7, MaxSessions)]
    [InlineData("hash", UserSession.MaxDeviceNameLength + 1, 10, 7, MaxSessions)]
    [InlineData("hash", 10, UserSession.MaxIpAddressLength + 1, 7, MaxSessions)]
    [InlineData("hash", 10, 10, 0, MaxSessions)]
    [InlineData("hash", 10, 10, 7, 0)]
    public void StartSession_con_datos_invalidos_lanza_DomainException(
        string refreshTokenHash,
        int deviceNameLength,
        int ipAddressLength,
        int lifetimeDays,
        int maxActiveSessions)
    {
        var user = ConfirmedUser();

        Should.Throw<DomainException>(() => user.StartSession(
            refreshTokenHash,
            new string('d', deviceNameLength),
            new string('1', ipAddressLength),
            Now,
            TimeSpan.FromDays(lifetimeDays),
            maxActiveSessions));
    }

    [Fact]
    public void Revoke_de_una_sesion_ya_revocada_conserva_la_fecha_y_el_motivo()
    {
        var user = ConfirmedUser();
        var first = StartSessions(user, MaxSessions)[0];
        var limitAt = Now.AddMinutes(MaxSessions);
        user.StartSession("hash-sexta", DeviceName, IpAddress, limitAt, Lifetime, MaxSessions);

        first.Revoke(SessionRevocationReason.Logout, limitAt.AddMinutes(1));

        first.RevokedAt.ShouldBe(limitAt);
        first.RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
    }

    private static User ConfirmedUser()
    {
        var user = UserMother.Pending();
        user.ConfirmEmail(UserMother.DefaultTokenHash, Now);
        return user;
    }

    // Abre las sesiones con un minuto de diferencia, así la primera es la usada hace más tiempo.
    private static List<UserSession> StartSessions(User user, int count) =>
        Enumerable.Range(0, count)
            .Select(index => Start(user, Now.AddMinutes(index), $"hash-{index}").Value)
            .ToList();

    private static Result<UserSession> Start(User user, DateTimeOffset at, string refreshTokenHash = "hash-sesion") =>
        user.StartSession(refreshTokenHash, DeviceName, IpAddress, at, Lifetime, MaxSessions);
}
