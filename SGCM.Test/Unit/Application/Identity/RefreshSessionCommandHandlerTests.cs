using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

/// <summary>
/// Pruebas de la renovación de sesión en <see cref="RefreshSessionCommandHandler"/>.
/// </summary>
public sealed class RefreshSessionCommandHandlerTests
{
    private const string RefreshToken = "refresh-en-claro";

    private static readonly RefreshSessionCommand Command = new(RefreshToken);

    private static readonly SessionTokens Tokens = new(
        new AccessToken("access-token", UserMother.Now.AddMinutes(15)),
        "refresh-token-nuevo",
        UserMother.Now.AddDays(7),
        null);

    private readonly ISessionService _sessionService = Substitute.For<ISessionService>();
    private readonly RefreshSessionCommandHandler _handler;

    public RefreshSessionCommandHandlerTests()
    {
        _handler = new RefreshSessionCommandHandler(_sessionService);
    }

    [Fact]
    public async Task Refresh_con_exito_devuelve_los_tokens_sin_revocacion()
    {
        ServiceReturns(Result<SessionTokens>.Success(Tokens));

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Tokens.ShouldBe(Tokens);
        result.Value.SessionRevoked.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_con_token_reutilizado_devuelve_exito_con_la_revocacion()
    {
        ServiceReturns(Result<SessionTokens>.Failure(UserErrors.RefreshTokenReused));

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SessionRevoked.ShouldBeTrue();
        result.Value.Tokens.ShouldBeNull();
    }

    [Fact]
    public async Task Refresh_con_sesion_invalida_propaga_el_error()
    {
        ServiceReturns(Result<SessionTokens>.Failure(UserErrors.SessionInvalid));

        var result = await _handler.HandleAsync(Command, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.SessionInvalid);
    }

    [Fact]
    public async Task Refresh_pasa_el_token_y_la_cancelacion_al_servicio()
    {
        using var cancellation = new CancellationTokenSource();
        ServiceReturns(Result<SessionTokens>.Success(Tokens));

        await _handler.HandleAsync(Command, cancellation.Token);

        await _sessionService.Received(1).RefreshAsync(RefreshToken, cancellation.Token);
    }

    [Fact]
    public void ToString_del_comando_no_contiene_el_token()
    {
        Command.ToString().ShouldNotContain(RefreshToken);
    }

    [Fact]
    public void ToString_del_resultado_no_contiene_los_tokens()
    {
        var text = RefreshSessionResult.Renewed(Tokens).ToString();

        text.ShouldNotContain(Tokens.AccessToken.Value);
        text.ShouldNotContain(Tokens.RefreshToken);
    }

    private void ServiceReturns(Result<SessionTokens> result) =>
        _sessionService.RefreshAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(result);
}
