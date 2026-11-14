using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Commands.ChangePassword;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Unit.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class ChangePasswordCommandHandlerTests
{
    private const string CurrentPassword = "ClaveActual123";
    private const string NewPassword = "  ClaveNueva123  ";
    private const string CurrentHash = "hash-temporal";
    private readonly ICurrentUser _current = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordSessionScenario _data = new(mustChangePassword: false);
    private readonly ChangePasswordCommandHandler _handler;

    public ChangePasswordCommandHandlerTests()
    {
        _current.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(call =>
        { call[0] = _data.Owner.Id; call[1] = _data.Current.Id; return true; });
        _repository.GetByIdForUpdateAsync(_data.Owner.Id, Arg.Any<CancellationToken>()).Returns(_data.Owner);
        _hasher.Verify(CurrentHash, CurrentPassword).Returns(true);
        _hasher.Hash(Arg.Any<Password>()).Returns("hash-nuevo");
        _handler = new(_current, _repository, _hasher, _clock);
    }

    [Fact]
    public async Task Verifica_la_actual_cambia_el_hash_conserva_la_sesion_actual_y_revoca_las_demas()
    {
        var result = await _handler.HandleAsync(new(CurrentPassword, NewPassword), CancellationToken.None);

        result.Value.ShouldBe(Unit.Value);
        _data.Owner.PasswordHash.ShouldBe("hash-nuevo");
        _data.Current.RevokedAt.ShouldBeNull();
        _data.OtherActive.RevocationReason.ShouldBe(SessionRevocationReason.PasswordReset);
        _data.SecondActive.RevocationReason.ShouldBe(SessionRevocationReason.PasswordReset);
        Received.InOrder(() =>
        {
            _repository.GetByIdForUpdateAsync(_data.Owner.Id, CancellationToken.None);
            _hasher.Verify(CurrentHash, CurrentPassword);
            _hasher.Hash(Arg.Is<Password>(p => p.Value == NewPassword));
        });
    }

    [Theory]
    [InlineData(null, "User.CurrentPasswordRequired")]
    [InlineData("", "User.CurrentPasswordRequired")]
    public async Task Sin_contraseña_actual_no_consulta_identidad_ni_repositorio(string? current, string error)
    {
        var result = await _handler.HandleAsync(new(current!, NewPassword), CancellationToken.None);

        result.Error.Code.ShouldBe(error);
        _current.ReceivedCalls().ShouldBeEmpty();
        _repository.ReceivedCalls().ShouldBeEmpty();
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("1234567", "User.PasswordTooShort")]
    [InlineData("long", "User.PasswordTooLong")]
    public async Task Contraseña_nueva_invalida_no_consulta_identidad_ni_repositorio(string password, string error)
    {
        if (password == "long") password = new string('x', 129);

        var result = await _handler.HandleAsync(new(CurrentPassword, password), CancellationToken.None);

        result.Error.Code.ShouldBe(error);
        _current.ReceivedCalls().ShouldBeEmpty();
        _repository.ReceivedCalls().ShouldBeEmpty();
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Identidad_invalida_no_carga_usuario()
    {
        _current.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(false);

        var result = await _handler.HandleAsync(new(CurrentPassword, NewPassword), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        _repository.ReceivedCalls().ShouldBeEmpty();
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Usuario_inexistente_no_verifica_ni_genera_hash()
    {
        _repository.GetByIdForUpdateAsync(_data.Owner.Id, Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(new(CurrentPassword, NewPassword), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("inactive", "User.AccountInactive")]
    [InlineData("session", "User.SessionInvalid")]
    public async Task Comprobacion_previa_rechaza_sin_verificar_ni_mutar(string kind, string error)
    {
        if (kind == "inactive") _data.Owner.Deactivate();
        if (kind == "session") _data.Owner.EndSession(_data.Current.RefreshTokenHash, _clock.UtcNow);
        var before = PasswordSessionScenario.Snapshot(_data.Owner);

        var result = await _handler.HandleAsync(new(CurrentPassword, NewPassword), CancellationToken.None);

        result.Error.Code.ShouldBe(error);
        PasswordSessionScenario.Snapshot(_data.Owner).ShouldBe(before);
        _hasher.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Contraseña_actual_incorrecta_rechaza_sin_generar_hash_ni_mutar()
    {
        var before = PasswordSessionScenario.Snapshot(_data.Owner);

        var result = await _handler.HandleAsync(new("ClaveEquivocada1", NewPassword), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.CurrentPasswordIncorrect);
        PasswordSessionScenario.Snapshot(_data.Owner).ShouldBe(before);
        _hasher.DidNotReceive().Hash(Arg.Any<Password>());
    }

    [Fact]
    public async Task Contraseña_nueva_igual_a_la_actual_rechaza_sin_generar_hash_ni_mutar()
    {
        var before = PasswordSessionScenario.Snapshot(_data.Owner);

        var result = await _handler.HandleAsync(new(CurrentPassword, CurrentPassword), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.NewPasswordSameAsCurrent);
        PasswordSessionScenario.Snapshot(_data.Owner).ShouldBe(before);
        _hasher.DidNotReceive().Hash(Arg.Any<Password>());
    }

    [Fact]
    public async Task Mutador_revalida_la_sesion_y_su_error_se_propaga_sin_cambiar_el_hash()
    {
        _hasher.Hash(Arg.Any<Password>()).Returns(_ =>
        { _data.Owner.EndSession(_data.Current.RefreshTokenHash, _clock.UtcNow); return "hash-nuevo"; });

        var result = await _handler.HandleAsync(new(CurrentPassword, NewPassword), CancellationToken.None);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        _data.Owner.PasswordHash.ShouldBe(CurrentHash);
    }
}
