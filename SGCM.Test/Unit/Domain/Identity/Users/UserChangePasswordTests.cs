using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

public sealed class UserChangePasswordTests
{
    private const string NewPasswordHash = "hash-nueva";

    [Fact]
    public void Cambia_el_hash_conserva_la_sesion_actual_y_revoca_las_demas_activas()
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        var expected = PasswordSessionScenario.WithRevocations(data.Owner,
            [data.OtherActive.Id, data.SecondActive.Id], SessionRevocationReason.PasswordReset, PasswordSessionScenario.Now)
            with { PasswordHash = NewPasswordHash };
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.ChangePassword(NewPasswordHash, data.Current.Id, PasswordSessionScenario.Now);

        result.IsSuccess.ShouldBeTrue();
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(expected);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
        data.Current.RevokedAt.ShouldBeNull();
        data.Current.IsActiveAt(PasswordSessionScenario.Now).ShouldBeTrue();
    }

    [Fact]
    public void Se_puede_cambiar_varias_veces()
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        data.Owner.ChangePassword(NewPasswordHash, data.Current.Id, PasswordSessionScenario.Now).IsSuccess.ShouldBeTrue();

        var result = data.Owner.ChangePassword("hash-segunda", data.Current.Id, PasswordSessionScenario.Now.AddMinutes(1));

        result.IsSuccess.ShouldBeTrue();
        data.Owner.PasswordHash.ShouldBe("hash-segunda");
        data.OtherActive.RevokedAt.ShouldBe(PasswordSessionScenario.Now);
    }

    [Fact]
    public void Una_cuenta_inactiva_rechaza_el_cambio_sin_mutar()
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        data.Owner.Deactivate().IsSuccess.ShouldBeTrue();
        AssertRejected(data, data.Current.Id, "User.AccountInactive", ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void Una_sesion_no_positiva_o_inexistente_no_permite_un_cambio_parcial(int id)
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        AssertRejected(data, id, "User.SessionInvalid", ErrorType.Unauthorized);
    }

    [Fact]
    public void La_sesion_activa_de_otro_usuario_no_autoriza_el_cambio()
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        AssertRejected(data, data.ForeignCurrent.Id, "User.SessionInvalid", ErrorType.Unauthorized);
    }

    [Theory]
    [InlineData("revocada")]
    [InlineData("expirada")]
    [InlineData("limite")]
    public void Una_sesion_actual_no_activa_no_permite_un_cambio_parcial(string kind)
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        AssertRejected(data, data.InactiveSession(kind).Id, "User.SessionInvalid", ErrorType.Unauthorized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_hash_invalido_lanza_la_excepcion_existente_sin_mutar(string? hash)
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        var before = PasswordSessionScenario.Snapshot(data.Owner);

        Should.Throw<DomainException>(() => data.Owner.ChangePassword(hash!, data.Current.Id, PasswordSessionScenario.Now));

        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
    }

    [Theory]
    [InlineData("valida", null)]
    [InlineData("inactiva", "User.AccountInactive")]
    [InlineData("ajena", "User.SessionInvalid")]
    [InlineData("revocada", "User.SessionInvalid")]
    [InlineData("inexistente", "User.SessionInvalid")]
    public void Comprobacion_previa_no_modifica_ningun_estado(string kind, string? error)
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        if (kind == "inactiva") data.Owner.Deactivate();
        var id = kind switch
        {
            "ajena" => data.ForeignCurrent.Id,
            "revocada" => data.InactiveSession(kind).Id,
            "inexistente" => 999,
            _ => data.Current.Id
        };
        var before = PasswordSessionScenario.Snapshot(data.Owner);

        var result = data.Owner.CanChangePassword(id, PasswordSessionScenario.Now);

        result.IsSuccess.ShouldBe(error is null);
        if (error is not null) result.Error.Code.ShouldBe(error);
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
    }

    private static void AssertRejected(PasswordSessionScenario data, int currentSessionId, string code, ErrorType type)
    {
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.ChangePassword(NewPasswordHash, currentSessionId, PasswordSessionScenario.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }
}
