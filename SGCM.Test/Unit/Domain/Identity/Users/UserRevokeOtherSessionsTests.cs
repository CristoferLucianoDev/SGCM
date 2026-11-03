using System.Text.Json;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

public sealed class UserRevokeOtherSessionsTests
{
    [Theory]
    [InlineData(true, SessionRevocationReason.PasswordReset)]
    [InlineData(false, SessionRevocationReason.PasswordReset)]
    [InlineData(false, SessionRevocationReason.Logout)]
    public void Revoca_solo_las_otras_activas_conservando_usuario_actual_historial_y_sesiones_ajenas(
        bool mustChangePassword, SessionRevocationReason reason)
    {
        var data = new PasswordSessionScenario(mustChangePassword);
        var expected = PasswordSessionScenario.WithRevocations(data.Owner,
            [data.OtherActive.Id, data.SecondActive.Id], reason, PasswordSessionScenario.Now);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.RevokeOtherSessions(data.Current.Id, reason, PasswordSessionScenario.Now);

        result.IsSuccess.ShouldBeTrue();
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(expected);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void Rechaza_ID_no_positivo_o_inexistente_sin_mutar(int currentSessionId)
    {
        var data = new PasswordSessionScenario();
        AssertInvalidSession(data, currentSessionId);
    }

    [Fact]
    public void Rechaza_una_sesion_activa_de_otro_usuario_sin_mutar_ninguno()
    {
        var data = new PasswordSessionScenario();
        data.ForeignCurrent.IsActiveAt(PasswordSessionScenario.Now).ShouldBeTrue();
        AssertInvalidSession(data, data.ForeignCurrent.Id);
    }

    [Fact]
    public void El_ID_del_usuario_no_identifica_su_sesion_actual()
    {
        var data = new PasswordSessionScenario();
        data.Owner.Id.ShouldNotBe(data.Current.Id);
        AssertInvalidSession(data, data.Owner.Id);
    }

    [Theory]
    [InlineData("revocada")]
    [InlineData("expirada")]
    [InlineData("limite")]
    public void Rechaza_como_actual_una_sesion_no_activa_sin_mutar(string kind)
    {
        var data = new PasswordSessionScenario();
        var current = data.InactiveSession(kind);
        current.IsActiveAt(PasswordSessionScenario.Now).ShouldBeFalse();
        if (kind == "limite") current.ExpiresAt.ShouldBe(PasswordSessionScenario.Now);
        AssertInvalidSession(data, current.Id);
    }

    [Fact]
    public void Repetir_conserva_la_fecha_y_el_motivo_originales_y_no_exige_marca_temporal()
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        data.Owner.RevokeOtherSessions(data.Current.Id, SessionRevocationReason.PasswordReset,
            PasswordSessionScenario.Now).IsSuccess.ShouldBeTrue();
        var before = PasswordSessionScenario.Snapshot(data.Owner);

        var result = data.Owner.RevokeOtherSessions(data.Current.Id, SessionRevocationReason.Logout,
            PasswordSessionScenario.Now.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
    }

    [Fact]
    public void Sin_otras_sesiones_activas_termina_sin_cambios()
    {
        var owner = User.CreateDoctor(Email.Create("unica@correo.com").Value, "hash-temporal");
        var current = PasswordSessionScenario.Start(owner, 501, "unica", PasswordSessionScenario.Now,
            TimeSpan.FromDays(7));
        var before = PasswordSessionScenario.Snapshot(owner);

        var result = owner.RevokeOtherSessions(current.Id, SessionRevocationReason.PasswordReset,
            PasswordSessionScenario.Now);

        result.IsSuccess.ShouldBeTrue();
        PasswordSessionScenario.Snapshot(owner).ShouldBe(before);
    }

    [Fact]
    public void Motivo_fuera_del_enum_es_un_argumento_invalido_y_no_revoca_parcialmente()
    {
        var data = new PasswordSessionScenario();
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        Should.Throw<DomainException>(() => data.Owner.RevokeOtherSessions(data.Current.Id,
            (SessionRevocationReason)(-1), PasswordSessionScenario.Now));

        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }

    private static void AssertInvalidSession(PasswordSessionScenario data, int id)
    {
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.RevokeOtherSessions(id, SessionRevocationReason.PasswordReset,
            PasswordSessionScenario.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.SessionInvalid");
        result.Error.Type.ShouldBe(ErrorType.Unauthorized);
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }
}

// Preparación compartida solo por las dos clases nuevas: sin base de datos ni servicios.
internal sealed class PasswordSessionScenario
{
    internal static readonly DateTimeOffset Now = UserMother.Now;

    internal User Owner { get; }
    internal User ForeignOwner { get; }
    internal UserSession Current { get; }
    internal UserSession OtherActive { get; }
    internal UserSession SecondActive { get; }
    internal UserSession Revoked { get; }
    internal UserSession Expired { get; }
    internal UserSession ExpiresNow { get; }
    internal UserSession ForeignCurrent { get; }

    internal PasswordSessionScenario(bool mustChangePassword = true)
    {
        var email = Email.Create("doctor@correo.com").Value;
        Owner = mustChangePassword
            ? User.CreateDoctor(email, "hash-temporal")
            : UserMother.WithRole(UserRole.Doctor, mustChangePassword: false, email: email, passwordHash: "hash-temporal");
        ForeignOwner = UserMother.Confirmed(Email.Create("otro@correo.com").Value, "hash-ajeno");
        SetId(Owner, 41);
        SetId(ForeignOwner, 61);

        Current = Start(Owner, 101, "actual", Now.AddMinutes(-10), TimeSpan.FromDays(7));
        OtherActive = Start(Owner, 102, "otra", Now.AddMinutes(-15), TimeSpan.FromDays(7));
        SecondActive = Start(Owner, 103, "segunda", Now.AddMinutes(-12), TimeSpan.FromDays(7));
        Revoked = Start(Owner, 104, "revocada", Now.AddHours(-1), TimeSpan.FromDays(7));
        Owner.EndSession("revocada", Now.AddMinutes(-20));
        Expired = Start(Owner, 105, "expirada", Now.AddHours(-2), TimeSpan.FromHours(1));
        ExpiresNow = Start(Owner, 106, "limite", Now.AddHours(-1), TimeSpan.FromHours(1));
        ForeignCurrent = Start(ForeignOwner, 201, "ajena", Now.AddMinutes(-9), TimeSpan.FromDays(7));
        Start(ForeignOwner, 202, "ajena-segunda", Now.AddMinutes(-8), TimeSpan.FromDays(7));

        Owner.RefreshSession("actual", "actual-rotada", Now.AddMinutes(-1), TimeSpan.FromDays(7),
            TimeSpan.FromSeconds(30)).IsSuccess.ShouldBeTrue();
        Owner.RefreshSession("otra", "otra-rotada", Now.AddMinutes(-2), TimeSpan.FromDays(7),
            TimeSpan.FromSeconds(30)).IsSuccess.ShouldBeTrue();
        Owner.IssueToken(UserTokenType.PasswordReset, "token-control", "hash-token-control", Now, TimeSpan.FromMinutes(10));

        Current.IsActiveAt(Now).ShouldBeTrue();
        OtherActive.IsActiveAt(Now).ShouldBeTrue();
        SecondActive.IsActiveAt(Now).ShouldBeTrue();
        Revoked.RevocationReason.ShouldBe(SessionRevocationReason.Logout);
        Expired.IsActiveAt(Now).ShouldBeFalse();
        ExpiresNow.ExpiresAt.ShouldBe(Now);
        Owner.Sessions.Select(session => session.Id).ShouldBe([101, 102, 103, 104, 105, 106]);
    }

    internal UserSession InactiveSession(string kind) => kind switch
    {
        "revocada" => Revoked,
        "expirada" => Expired,
        "limite" => ExpiresNow,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    internal static UserSession Start(User owner, int id, string hash, DateTimeOffset at, TimeSpan lifetime)
    {
        var result = owner.StartSession(hash, $"Dispositivo {id}", "203.0.113.10", at, lifetime, maxActiveSessions: 20);
        result.IsSuccess.ShouldBeTrue();
        SetId(result.Value, id);
        return result.Value;
    }

    private static void SetId(Entity entity, int id) =>
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);

    // Todos los campos son valores copiados; no se guardan referencias a entidades mutables.
    internal sealed record SessionState(int Id, string Hash, string? PreviousHash, string? Device, string? Ip,
        DateTimeOffset ExpiresAt, DateTimeOffset LastUsedAt, DateTimeOffset? RevokedAt, SessionRevocationReason? Reason)
    {
        internal static SessionState From(UserSession session) => new(session.Id, session.RefreshTokenHash,
            session.PreviousRefreshTokenHash, session.DeviceName, session.IpAddress, session.ExpiresAt,
            session.LastUsedAt, session.RevokedAt, session.RevocationReason);
    }

    internal sealed record UserState(string Details, string PasswordHash, bool MustChangePassword, string Sessions);

    internal static UserState Snapshot(User owner) => new(
        JsonSerializer.Serialize(new
        {
            owner.Id, Email = owner.Email.Value, owner.Role, owner.IsActive, owner.IsEmailConfirmed,
            Tokens = owner.Tokens.Select(token => new
            {
                token.Id, token.Type, token.TokenHash, token.IssuedAt, token.ExpiresAt, token.UsedAt, token.InvalidatedAt
            }).ToArray(),
            Events = owner.DomainEvents.Select(value => JsonSerializer.Serialize(value, value.GetType())).ToArray()
        }), owner.PasswordHash, owner.MustChangePassword,
        JsonSerializer.Serialize(owner.Sessions.Select(SessionState.From).ToArray()));

    internal static UserState WithRevocations(User owner, int[] ids, SessionRevocationReason reason, DateTimeOffset at)
    {
        var before = Snapshot(owner);
        var expected = owner.Sessions.Select(SessionState.From)
            .Select(state => ids.Contains(state.Id) ? state with { RevokedAt = at, Reason = reason } : state).ToArray();
        return before with { Sessions = JsonSerializer.Serialize(expected) };
    }
}
