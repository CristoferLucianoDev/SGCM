using NSubstitute;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Unit.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ChangeTemporaryPasswordCommandHandlerTests
{
    private const string Candidate = "  NuevaClave123  ";
    private readonly ICurrentUser _current = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _repository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _hasher = Substitute.For<IPasswordHasher>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly PasswordSessionScenario _data = new();
    private readonly ChangeTemporaryPasswordCommandHandler _handler;
    private readonly SessionTokens _tokens = new(new("jwt-nuevo", UserMother.Now.AddMinutes(15)),
        "refresh-nuevo", UserMother.Now.AddDays(7), PendingStep.DoctorProfile);

    public ChangeTemporaryPasswordCommandHandlerTests()
    {
        _current.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(call =>
        { call[0] = _data.Owner.Id; call[1] = _data.Current.Id; return true; });
        _repository.GetByIdForUpdateAsync(_data.Owner.Id, Arg.Any<CancellationToken>()).Returns(_data.Owner);
        _hasher.Hash(Arg.Any<Password>()).Returns("hash-nuevo");
        _sessions.RenewCurrentAsync(_data.Owner, _data.Current.Id, _clock.UtcNow, Arg.Any<CancellationToken>())
            .Returns(_tokens);
        _handler = new(_current, _repository, _hasher, _clock, _sessions);
    }

    [Fact]
    public async Task Carga_bloqueada_antes_de_verify_y_hash_y_renueva_la_misma_sesion()
    {
        var result = await _handler.HandleAsync(new(Candidate), CancellationToken.None);
        result.Value.ShouldBeSameAs(_tokens);
        _data.Owner.PasswordHash.ShouldBe("hash-nuevo");
        _data.Owner.MustChangePassword.ShouldBeFalse();
        _data.Current.RevokedAt.ShouldBeNull();
        _data.OtherActive.RevocationReason.ShouldBe(SessionRevocationReason.PasswordReset);
        Received.InOrder(() =>
        {
            _repository.GetByIdForUpdateAsync(_data.Owner.Id, CancellationToken.None);
            _hasher.Verify("hash-temporal", Candidate);
            _hasher.Hash(Arg.Is<Password>(p => p.Value == Candidate));
            _sessions.RenewCurrentAsync(_data.Owner, _data.Current.Id, _clock.UtcNow, CancellationToken.None);
        });
        _repository.ReceivedCalls().Count().ShouldBe(1);
        await _sessions.DidNotReceiveWithAnyArgs().StartAsync(default!, default, default, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    [InlineData("long")]
    public async Task Entrada_invalida_no_consulta_identidad_ni_repositorio(string? password)
    {
        if (password == "long") password = new string('x', 129);
        var result = await _handler.HandleAsync(new(password!), CancellationToken.None);
        result.IsFailure.ShouldBeTrue();
        _current.ReceivedCalls().ShouldBeEmpty();
        _repository.ReceivedCalls().ShouldBeEmpty();
        AssertNoHashOrRenewal();
    }

    [Fact]
    public async Task Identidad_invalida_no_carga_usuario()
    {
        _current.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(false);
        var result = await _handler.HandleAsync(new(Candidate), CancellationToken.None);
        result.Error.ShouldBe(UserErrors.SessionInvalid);
        _repository.ReceivedCalls().ShouldBeEmpty();
        AssertNoHashOrRenewal();
    }

    [Fact]
    public async Task Usuario_inexistente_no_verifica_ni_genera_hash()
    {
        _repository.GetByIdForUpdateAsync(_data.Owner.Id, Arg.Any<CancellationToken>()).Returns((User?)null);
        (await _handler.HandleAsync(new(Candidate), CancellationToken.None)).Error.ShouldBe(UserErrors.SessionInvalid);
        AssertNoHashOrRenewal();
    }

    [Theory]
    [InlineData("inactive", "User.AccountInactive")]
    [InlineData("flag", "User.TemporaryPasswordChangeNotRequired")]
    [InlineData("session", "User.SessionInvalid")]
    public async Task Comprobacion_previa_rechaza_sin_hash_ni_mutaciones(string kind, string error)
    {
        if (kind == "inactive") _data.Owner.Deactivate();
        if (kind == "flag")
            _data.Owner.ChangeTemporaryPassword("hash-ya-cambiado", _data.Current.Id, _clock.UtcNow);
        if (kind == "session") _data.Owner.EndSession(_data.Current.RefreshTokenHash, _clock.UtcNow);
        var before = PasswordSessionScenario.Snapshot(_data.Owner);
        var result = await _handler.HandleAsync(new(Candidate), CancellationToken.None);
        result.Error.Code.ShouldBe(error);
        PasswordSessionScenario.Snapshot(_data.Owner).ShouldBe(before);
        AssertNoHashOrRenewal();
    }

    [Fact]
    public async Task Contraseña_igual_no_genera_hash_ni_muta_ni_emite_tokens()
    {
        _hasher.Verify(_data.Owner.PasswordHash, Candidate).Returns(true);
        var before = PasswordSessionScenario.Snapshot(_data.Owner);
        (await _handler.HandleAsync(new(Candidate), CancellationToken.None)).Error.ShouldBe(UserErrors.PasswordUnchanged);
        PasswordSessionScenario.Snapshot(_data.Owner).ShouldBe(before);
        _hasher.DidNotReceive().Hash(Arg.Any<Password>());
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Mutador_revalida_y_su_error_impide_renovar()
    {
        _hasher.Hash(Arg.Any<Password>()).Returns(_ =>
        { _data.Owner.EndSession(_data.Current.RefreshTokenHash, _clock.UtcNow); return "hash-nuevo"; });
        (await _handler.HandleAsync(new(Candidate), CancellationToken.None)).Error.ShouldBe(UserErrors.SessionInvalid);
        _data.Owner.PasswordHash.ShouldBe("hash-temporal");
        _data.Owner.MustChangePassword.ShouldBeTrue();
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Error_de_renovacion_se_propaga_para_que_el_decorador_revierta()
    {
        _sessions.RenewCurrentAsync(_data.Owner, _data.Current.Id, _clock.UtcNow, Arg.Any<CancellationToken>())
            .Returns(UserErrors.SessionInvalid);
        (await _handler.HandleAsync(new(Candidate), CancellationToken.None)).Error.ShouldBe(UserErrors.SessionInvalid);
    }

    private void AssertNoHashOrRenewal()
    {
        _hasher.ReceivedCalls().ShouldBeEmpty();
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }
}

