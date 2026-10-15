using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.Commands.RequestPasswordReset;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class RequestPasswordResetCommandHandlerTests
{
    private const string AttemptKey = "password-reset:paciente@correo.com";
    private const int PasswordResetMinutes = 10;
    private const int MaxRequestsPerEmail = 3;
    private const int EmailWindowMinutes = 15;

    private static readonly SecureToken NewToken = new("raw-reset", "hash-reset");
    private static readonly RequestPasswordResetCommand Command = new("paciente@correo.com");

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly IAttemptLimiter _attemptLimiter = Substitute.For<IAttemptLimiter>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly RequestPasswordResetCommandHandler _handler;

    public RequestPasswordResetCommandHandlerTests()
    {
        _tokenGenerator.Generate().Returns(NewToken);
        _attemptLimiter.TryRegisterAttempt(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>()).Returns(true);

        var accountsOptions = Options.Create(new AccountsOptions { PasswordResetTokenMinutes = PasswordResetMinutes });
        var limitsOptions = Options.Create(new PasswordResetLimitsOptions
        {
            MaxRequestsPerEmail = MaxRequestsPerEmail,
            EmailWindowMinutes = EmailWindowMinutes
        });

        _handler = new RequestPasswordResetCommandHandler(
            _userRepository, _tokenGenerator, _dateTimeProvider, _attemptLimiter, accountsOptions, limitsOptions);
    }

    [Fact]
    public async Task Solicitud_de_una_cuenta_confirmada_y_activa_emite_el_enlace()
    {
        var user = UserMother.Confirmed();
        ReturnsUser(user);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var token = user.Tokens.Single(token => token.Type == UserTokenType.PasswordReset);
        token.TokenHash.ShouldBe(NewToken.Hash);
        token.ExpiresAt.ShouldBe(_dateTimeProvider.UtcNow.AddMinutes(PasswordResetMinutes));
        var issued = user.DomainEvents.OfType<UserTokenIssuedEvent>().Last();
        issued.Type.ShouldBe(UserTokenType.PasswordReset);
        issued.RawToken.ShouldBe(NewToken.Value);
    }

    [Fact]
    public async Task Solicitud_carga_al_usuario_con_la_fila_bloqueada()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        await _userRepository.Received(1).GetByEmailForUpdateAsync(
            Email.Create(Command.Email).Value, Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Solicitud_de_un_correo_inexistente_responde_exito_sin_emitir_enlace()
    {
        ReturnsUser(null);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _tokenGenerator.DidNotReceive().Generate();
    }

    [Fact]
    public async Task Solicitud_de_una_cuenta_pendiente_responde_exito_sin_emitir_enlace()
    {
        var user = UserMother.Pending();
        ReturnsUser(user);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.Tokens.ShouldNotContain(token => token.Type == UserTokenType.PasswordReset);
        _tokenGenerator.DidNotReceive().Generate();
    }

    [Fact]
    public async Task Solicitud_de_una_cuenta_inactiva_responde_exito_sin_emitir_enlace()
    {
        var user = UserMother.Confirmed();
        user.Deactivate();
        ReturnsUser(user);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        user.Tokens.ShouldNotContain(token => token.Type == UserTokenType.PasswordReset);
        _tokenGenerator.DidNotReceive().Generate();
    }

    [Fact]
    public async Task Solicitud_rechazada_por_el_limite_no_consulta_el_repositorio()
    {
        _attemptLimiter.TryRegisterAttempt(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>()).Returns(false);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.TooManyPasswordResetRequests);
        _userRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Solicitud_de_un_correo_inexistente_registra_el_intento_con_la_clave_normalizada()
    {
        ReturnsUser(null);

        await _handler.HandleAsync(Command with { Email = "  Paciente@Correo.COM " }, CancellationToken.None);

        _attemptLimiter.Received(1).TryRegisterAttempt(
            AttemptKey, MaxRequestsPerEmail, TimeSpan.FromMinutes(EmailWindowMinutes));
        _attemptLimiter.DidNotReceive().Reset(Arg.Any<string>());
    }

    [Fact]
    public async Task Solicitud_con_correo_invalido_se_rechaza_sin_registrar_el_intento()
    {
        var result = await _handler.HandleAsync(Command with { Email = "no-es-un-correo" }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.InvalidEmail.Code);
        _attemptLimiter.ReceivedCalls().ShouldBeEmpty();
        _userRepository.ReceivedCalls().ShouldBeEmpty();
    }

    private void ReturnsUser(User? user) =>
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(user);
}
