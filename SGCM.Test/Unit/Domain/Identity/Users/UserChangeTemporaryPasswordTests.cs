using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

public sealed class UserChangeTemporaryPasswordTests
{
    private const string NewPasswordHash = "hash-nueva";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Cambia_hash_y_marca_revoca_otras_activas_y_conserva_el_resto_del_agregado(bool hasProfile)
    {
        var data = new PasswordSessionScenario();
        data.Owner.MustChangePassword.ShouldBeTrue();
        data.Owner.PasswordHash.ShouldNotBe(NewPasswordHash);
        data.Owner.GetPendingStep(hasProfile).ShouldBe(PendingStep.ChangeTemporaryPassword);
        var expected = PasswordSessionScenario.WithRevocations(data.Owner,
            [data.OtherActive.Id, data.SecondActive.Id], SessionRevocationReason.PasswordReset, PasswordSessionScenario.Now)
            with { PasswordHash = NewPasswordHash, MustChangePassword = false };
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.ChangeTemporaryPassword(NewPasswordHash, data.Current.Id, PasswordSessionScenario.Now);

        result.IsSuccess.ShouldBeTrue();
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(expected);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
        data.Owner.GetPendingStep(hasProfile).ShouldBe(hasProfile ? (PendingStep?)null : PendingStep.DoctorProfile);
    }

    [Fact]
    public void Sin_marca_temporal_rechaza_el_cambio_sin_mutar()
    {
        var data = new PasswordSessionScenario(mustChangePassword: false);
        AssertRejected(data, data.Current.Id, "User.TemporaryPasswordChangeNotRequired", ErrorType.Conflict);
    }

    [Fact]
    public void Un_segundo_cambio_temporal_no_modifica_hash_ni_sesiones()
    {
        var data = new PasswordSessionScenario();
        data.Owner.ChangeTemporaryPassword(NewPasswordHash, data.Current.Id, PasswordSessionScenario.Now)
            .IsSuccess.ShouldBeTrue();
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.ChangeTemporaryPassword("hash-segunda", data.Current.Id,
            PasswordSessionScenario.Now.AddMinutes(1));

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("User.TemporaryPasswordChangeNotRequired");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }

    [Fact]
    public void Una_cuenta_inactiva_rechaza_el_cambio_sin_mutar()
    {
        var data = new PasswordSessionScenario();
        data.Owner.Deactivate().IsSuccess.ShouldBeTrue();
        AssertRejected(data, data.Current.Id, "User.AccountInactive", ErrorType.Forbidden);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_hash_obligatorio_invalido_lanza_la_excepcion_existente_sin_mutar(string? hash)
    {
        var data = new PasswordSessionScenario();
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        Should.Throw<DomainException>(() => data.Owner.ChangeTemporaryPassword(hash!, data.Current.Id,
            PasswordSessionScenario.Now));

        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void Una_sesion_no_positiva_o_inexistente_no_permite_un_cambio_parcial(int id)
    {
        var data = new PasswordSessionScenario();
        AssertRejected(data, id, "User.SessionInvalid", ErrorType.Unauthorized);
    }

    [Fact]
    public void La_sesion_activa_de_otro_usuario_no_autoriza_el_cambio()
    {
        var data = new PasswordSessionScenario();
        data.ForeignCurrent.IsActiveAt(PasswordSessionScenario.Now).ShouldBeTrue();
        AssertRejected(data, data.ForeignCurrent.Id, "User.SessionInvalid", ErrorType.Unauthorized);
    }

    [Theory]
    [InlineData("revocada")]
    [InlineData("expirada")]
    [InlineData("limite")]
    public void Una_sesion_actual_no_activa_no_permite_un_cambio_parcial(string kind)
    {
        var data = new PasswordSessionScenario();
        var current = data.InactiveSession(kind);
        current.IsActiveAt(PasswordSessionScenario.Now).ShouldBeFalse();
        if (kind == "limite") current.ExpiresAt.ShouldBe(PasswordSessionScenario.Now);
        AssertRejected(data, current.Id, "User.SessionInvalid", ErrorType.Unauthorized);
    }

    [Fact]
    public void La_sesion_conservada_puede_rotar_y_una_sesion_revocada_no_puede_renovarse()
    {
        var data = new PasswordSessionScenario();
        data.Owner.ChangeTemporaryPassword(NewPasswordHash, data.Current.Id, PasswordSessionScenario.Now)
            .IsSuccess.ShouldBeTrue();
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var at = PasswordSessionScenario.Now.AddSeconds(1);

        var rejected = data.Owner.RefreshSession(data.OtherActive.RefreshTokenHash, "otra-posterior", at,
            TimeSpan.FromDays(7), TimeSpan.FromSeconds(30));

        rejected.Error.Code.ShouldBe("User.SessionInvalid");
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        var renewed = data.Owner.RefreshSession(data.Current.RefreshTokenHash, "actual-posterior", at,
            TimeSpan.FromDays(7), TimeSpan.FromSeconds(30));
        renewed.IsSuccess.ShouldBeTrue();
        data.Current.Id.ShouldBe(101);
        data.Current.RefreshTokenHash.ShouldBe("actual-posterior");
        data.Current.PreviousRefreshTokenHash.ShouldBe("actual-rotada");
        data.Current.RevokedAt.ShouldBeNull();
    }

    private static void AssertRejected(PasswordSessionScenario data, int currentSessionId, string code, ErrorType type)
    {
        var before = PasswordSessionScenario.Snapshot(data.Owner);
        var foreignBefore = PasswordSessionScenario.Snapshot(data.ForeignOwner);

        var result = data.Owner.ChangeTemporaryPassword(NewPasswordHash, currentSessionId, PasswordSessionScenario.Now);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        PasswordSessionScenario.Snapshot(data.Owner).ShouldBe(before);
        PasswordSessionScenario.Snapshot(data.ForeignOwner).ShouldBe(foreignBefore);
    }
}
