using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Logout;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using SGCM.Web.Api.Controllers.Identity;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

/// <summary>
/// Pruebas de la renovación y el cierre de sesión en <see cref="AuthController"/>.
/// </summary>
public sealed class AuthControllerTests
{
    private const string RefreshToken = "refresh-en-claro";
    private const int NoContentStatus = 204;
    private const int BadRequestStatus = 400;
    private const int UnauthorizedStatus = 401;

    private static readonly SessionTokens Tokens = new(
        new AccessToken("access-token", UserMother.Now.AddMinutes(15)),
        "refresh-token-nuevo",
        UserMother.Now.AddDays(7),
        null);

    private readonly ICommandHandler<RefreshSessionCommand, RefreshSessionResult> _refreshHandler =
        Substitute.For<ICommandHandler<RefreshSessionCommand, RefreshSessionResult>>();

    private readonly ICommandHandler<LogoutCommand, Unit> _logoutHandler =
        Substitute.For<ICommandHandler<LogoutCommand, Unit>>();

    [Fact]
    public async Task Refresh_mapea_la_solicitud_y_devuelve_200_con_los_tokens()
    {
        RefreshReturns(Result<RefreshSessionResult>.Success(RefreshSessionResult.Renewed(Tokens)));

        var result = await new AuthController().Refresh(
            new RefreshSessionRequest(RefreshToken), _refreshHandler);

        var response = result.ShouldBeOfType<OkObjectResult>();
        var body = response.Value.ShouldBeOfType<RefreshSessionResponse>();
        body.AccessToken.ShouldBe(Tokens.AccessToken.Value);
        body.RefreshToken.ShouldBe(Tokens.RefreshToken);
        await _refreshHandler.Received(1).HandleAsync(new RefreshSessionCommand(RefreshToken), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Refresh_llama_al_handler_sin_cancelacion_para_que_el_cliente_no_revierta_la_revocacion()
    {
        RefreshReturns(Result<RefreshSessionResult>.Success(RefreshSessionResult.Revoked()));

        await new AuthController().Refresh(new RefreshSessionRequest(RefreshToken), _refreshHandler);

        await _refreshHandler.Received(1).HandleAsync(Arg.Any<RefreshSessionCommand>(), CancellationToken.None);
    }

    [Fact]
    public async Task Refresh_con_sesiones_revocadas_devuelve_401_sin_revelar_la_reutilizacion()
    {
        RefreshReturns(Result<RefreshSessionResult>.Success(RefreshSessionResult.Revoked()));

        var result = await new AuthController().Refresh(
            new RefreshSessionRequest(RefreshToken), _refreshHandler);

        var problem = ShouldBeProblem(result, UnauthorizedStatus);
        problem.Extensions["code"].ShouldBe(UserErrors.SessionInvalid.Code);
        problem.Extensions["code"].ShouldNotBe(UserErrors.RefreshTokenReused.Code);
    }

    [Fact]
    public async Task Refresh_con_sesion_invalida_devuelve_401()
    {
        RefreshReturns(Result<RefreshSessionResult>.Failure(UserErrors.SessionInvalid));

        var result = await new AuthController().Refresh(
            new RefreshSessionRequest(RefreshToken), _refreshHandler);

        var problem = ShouldBeProblem(result, UnauthorizedStatus);
        problem.Extensions["code"].ShouldBe(UserErrors.SessionInvalid.Code);
    }

    [Fact]
    public async Task Refresh_con_error_de_validacion_devuelve_400()
    {
        RefreshReturns(Result<RefreshSessionResult>.Failure(UserErrors.RefreshTokenRequired));

        var result = await new AuthController().Refresh(
            new RefreshSessionRequest(string.Empty), _refreshHandler);

        var problem = ShouldBeProblem(result, BadRequestStatus);
        problem.Extensions["code"].ShouldBe(UserErrors.RefreshTokenRequired.Code);
    }

    [Fact]
    public async Task Logout_mapea_la_solicitud_y_devuelve_204()
    {
        LogoutReturns(Result<Unit>.Success(Unit.Value));

        var result = await new AuthController().Logout(
            new LogoutRequest(RefreshToken), _logoutHandler);

        result.ShouldBeOfType<NoContentResult>().StatusCode.ShouldBe(NoContentStatus);
        await _logoutHandler.Received(1).HandleAsync(new LogoutCommand(RefreshToken), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logout_llama_al_handler_sin_cancelacion_para_que_el_cliente_no_revierta_el_cierre()
    {
        LogoutReturns(Result<Unit>.Success(Unit.Value));

        await new AuthController().Logout(new LogoutRequest(RefreshToken), _logoutHandler);

        await _logoutHandler.Received(1).HandleAsync(Arg.Any<LogoutCommand>(), CancellationToken.None);
    }

    [Fact]
    public async Task Logout_con_error_de_validacion_devuelve_400()
    {
        LogoutReturns(Result<Unit>.Failure(UserErrors.RefreshTokenRequired));

        var result = await new AuthController().Logout(
            new LogoutRequest(string.Empty), _logoutHandler);

        var problem = ShouldBeProblem(result, BadRequestStatus);
        problem.Extensions["code"].ShouldBe(UserErrors.RefreshTokenRequired.Code);
    }

    [Fact]
    public void Login_prohibe_guardar_la_respuesta_porque_lleva_tokens()
    {
        ShouldForbidStoringResponse(nameof(AuthController.Login));
    }

    [Fact]
    public void Refresh_prohibe_guardar_la_respuesta_porque_lleva_tokens()
    {
        ShouldForbidStoringResponse(nameof(AuthController.Refresh));
    }

    private static void ShouldForbidStoringResponse(string actionName)
    {
        var attribute = typeof(AuthController).GetMethod(actionName)!.GetCustomAttribute<ResponseCacheAttribute>();

        attribute.ShouldNotBeNull();
        attribute.NoStore.ShouldBeTrue();
        attribute.Location.ShouldBe(ResponseCacheLocation.None);
    }

    private void RefreshReturns(Result<RefreshSessionResult> result) =>
        _refreshHandler.HandleAsync(Arg.Any<RefreshSessionCommand>(), Arg.Any<CancellationToken>()).Returns(result);

    private void LogoutReturns(Result<Unit> result) =>
        _logoutHandler.HandleAsync(Arg.Any<LogoutCommand>(), Arg.Any<CancellationToken>()).Returns(result);

    private static ProblemDetails ShouldBeProblem(IActionResult result, int expectedStatus)
    {
        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(expectedStatus);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(expectedStatus);
        return problem;
    }
}
