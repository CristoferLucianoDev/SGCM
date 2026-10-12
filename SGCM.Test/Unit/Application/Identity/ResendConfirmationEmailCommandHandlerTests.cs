using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.Commands.ResendConfirmationEmail;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ResendConfirmationEmailCommandHandlerTests
{
    private const int ConfirmationMinutes = 15;
    private const int CooldownSeconds = 30;

    private static readonly SecureToken NewToken = new("raw-nuevo", "hash-nuevo");
    private static readonly ResendConfirmationEmailCommand Command = new("paciente@correo.com");

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly ISecureTokenGenerator _tokenGenerator = Substitute.For<ISecureTokenGenerator>();
    private readonly FakeDateTimeProvider _dateTimeProvider = new();
    private readonly ResendConfirmationEmailCommandHandler _handler;

    public ResendConfirmationEmailCommandHandlerTests()
    {
        _tokenGenerator.Generate().Returns(NewToken);

        var options = Options.Create(new AccountsOptions
        {
            EmailConfirmationTokenMinutes = ConfirmationMinutes,
            ResendConfirmationCooldownSeconds = CooldownSeconds
        });

        _handler = new ResendConfirmationEmailCommandHandler(_userRepository, _tokenGenerator, _dateTimeProvider, options);
    }

    [Fact]
    public async Task Reenvio_tras_la_espera_emite_un_token_nuevo()
    {
        var pending = UserMother.Pending();
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(pending);
        _dateTimeProvider.UtcNow = UserMother.Now.AddSeconds(CooldownSeconds);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var token = pending.Tokens.Single(token => token.TokenHash == NewToken.Hash);
        token.IsValidAt(_dateTimeProvider.UtcNow).ShouldBeTrue();
        token.ExpiresAt.ShouldBe(_dateTimeProvider.UtcNow.AddMinutes(ConfirmationMinutes));
        pending.DomainEvents.OfType<UserTokenIssuedEvent>().Last().RawToken.ShouldBe(NewToken.Value);
    }

    [Fact]
    public async Task Reenvio_carga_al_usuario_con_la_fila_bloqueada()
    {
        await _handler.HandleAsync(Command, CancellationToken.None);

        await _userRepository.Received(1).GetByEmailForUpdateAsync(
            Email.Create(Command.Email).Value, Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reenvio_de_un_correo_sin_registro_indica_registrarse_de_nuevo()
    {
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns((User?)null);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.RegistrationExpired);
    }

    [Fact]
    public async Task Reenvio_antes_de_la_espera_devuelve_el_error_del_dominio()
    {
        _userRepository.GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>()).Returns(UserMother.Pending());
        _dateTimeProvider.UtcNow = UserMother.Now.AddSeconds(10);

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.ResendTooSoon);
    }

    [Fact]
    public async Task Reenvio_con_correo_invalido_se_rechaza_sin_consultar_el_repositorio()
    {
        var result = await _handler.HandleAsync(Command with { Email = "no-es-un-correo" }, CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.InvalidEmail.Code);
        _userRepository.ReceivedCalls().ShouldBeEmpty();
    }
}
