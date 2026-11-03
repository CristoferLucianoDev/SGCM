using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Web.Contracts.Common;
using Microsoft.AspNetCore.Authorization;
using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using SGCM.Web.Api.Infrastructure;
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
    [Theory]
    [InlineData(null, false)]
    [InlineData(ClientTypeHeader.Web, false)]
    [InlineData(ClientTypeHeader.Mobile, true)]
    [InlineData("WEB", false)]
    [InlineData("Mobile", true)]
    [InlineData("  web  ", false)]
    [InlineData("  MOBILE  ", true)]
    public async Task Cliente_declarado_valido_conserva_login_y_adapta_solo_el_aviso(string? header, bool notice)
    {
        // Literales deliberados: fijan el contrato externo aunque cambien sus constantes.
        ClientTypeHeader.Name.ShouldBe("X-SGCM-Client");
        ClientTypeHeader.Web.ShouldBe("web");
        ClientTypeHeader.Mobile.ShouldBe("mobile");
        var controller = LoginController(header is null ? (StringValues?)null : new StringValues(header));
        var handler = Substitute.For<ICommandHandler<LoginCommand, LoginResult>>();
        var tokens = Tokens with { PendingStep = PendingStep.ChangeTemporaryPassword };
        handler.HandleAsync(Arg.Any<LoginCommand>(), Arg.Any<CancellationToken>())
            .Returns(LoginResult.Authenticated(tokens, hasPendingDoctorFirstAccess: true));
        using var cancellation = new CancellationTokenSource();

        var result = await controller.Login(new("doctor@correo.com", "  Clave123  "), handler, cancellation.Token);

        var body = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LoginResponse>();
        body.RequiresWebFirstAccess.ShouldBe(notice);
        body.PendingStep.ShouldBe("change_password");
        body.AccessToken.ShouldBe(tokens.AccessToken.Value);
        body.RefreshToken.ShouldBe(tokens.RefreshToken);
        await handler.Received(1).HandleAsync(new("doctor@correo.com", "  Clave123  ", "Agente no clasificador", "203.0.113.10"), cancellation.Token);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("unknown-client")]
    [InlineData("web,mobile")]
    [InlineData("mobile,web")]
    [InlineData("mobile,mobile")]
    public async Task Cliente_declarado_invalido_no_despacha_comando(string header)
    {
        await AssertInvalidClientAsync(new StringValues(header));
    }

    [Theory]
    [InlineData(ClientTypeHeader.Web, ClientTypeHeader.Web)]
    [InlineData(ClientTypeHeader.Mobile, ClientTypeHeader.Mobile)]
    [InlineData(ClientTypeHeader.Web, ClientTypeHeader.Mobile)]
    public async Task Cliente_declarado_multiple_no_selecciona_el_primer_valor(string first, string second)
    {
        await AssertInvalidClientAsync(new StringValues([first, second]));
    }

    [Fact]
    public async Task Cliente_declarado_no_se_toma_de_query_ni_UserAgent_y_conserva_errores()
    {
        var controller = LoginController(null);
        controller.Request.QueryString = new QueryString("?clientType=mobile");
        controller.Request.Headers.UserAgent = "Mobile";
        var handler = Substitute.For<ICommandHandler<LoginCommand, LoginResult>>();
        handler.HandleAsync(Arg.Any<LoginCommand>(), Arg.Any<CancellationToken>())
            .Returns(LoginResult.Authenticated(Tokens, hasPendingDoctorFirstAccess: true));
        var result = await controller.Login(new("doctor@correo.com", "Clave123"), handler, CancellationToken.None);
        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LoginResponse>().RequiresWebFirstAccess.ShouldBeFalse();
        handler.HandleAsync(Arg.Any<LoginCommand>(), Arg.Any<CancellationToken>()).Returns(UserErrors.InvalidCredentials);
        result = await controller.Login(new("doctor@correo.com", "Incorrecta"), handler, CancellationToken.None);
        ShouldBeProblem(result, 401).Extensions["code"].ShouldBe("User.InvalidCredentials");
    }

    private static AuthController LoginController(StringValues? header)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.10");
        context.Request.Headers.UserAgent = "Agente no clasificador";
        if (header.HasValue) context.Request.Headers[ClientTypeHeader.Name] = header.Value;
        return new AuthController { ControllerContext = new ControllerContext { HttpContext = context } };
    }

    private static async Task AssertInvalidClientAsync(StringValues header)
    {
        var handler = Substitute.For<ICommandHandler<LoginCommand, LoginResult>>();
        var result = await LoginController(header).Login(new("doctor@correo.com", "Clave123"), handler, CancellationToken.None);
        var problem = ShouldBeProblem(result, 400);
        problem.Extensions["code"].ShouldBe("Request.InvalidClientType");
        problem.Title.ShouldBe("El tipo de cliente debe ser web o mobile, con un único valor.");
        handler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Cambio_temporal_mapea_y_devuelve_LoginResponse()
    {
        var handler = Substitute.For<ICommandHandler<ChangeTemporaryPasswordCommand, SessionTokens>>();
        handler.HandleAsync(Arg.Any<ChangeTemporaryPasswordCommand>(), Arg.Any<CancellationToken>()).Returns(Tokens);
        using var cancellation = new CancellationTokenSource();
        var result = await new AuthController().ChangeTemporaryPassword(new("NuevaClave123"), handler, cancellation.Token);
        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LoginResponse>();
        response.RequiresTwoFactor.ShouldBeFalse();
        response.AccessToken.ShouldBe(Tokens.AccessToken.Value);
        response.RefreshToken.ShouldBe(Tokens.RefreshToken);
        await handler.Received(1).HandleAsync(new("NuevaClave123"), cancellation.Token);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(409)]
    public async Task Cambio_temporal_conserva_codigo_y_http_del_error(int status)
    {
        var error = status switch
        {
            400 => UserErrors.PasswordUnchanged,
            401 => UserErrors.SessionInvalid,
            403 => UserErrors.AccountInactive,
            _ => UserErrors.TemporaryPasswordChangeNotRequired
        };
        var handler = Substitute.For<ICommandHandler<ChangeTemporaryPasswordCommand, SessionTokens>>();
        handler.HandleAsync(Arg.Any<ChangeTemporaryPasswordCommand>(), Arg.Any<CancellationToken>()).Returns(error);
        var result = await new AuthController().ChangeTemporaryPassword(new("NuevaClave123"), handler, CancellationToken.None);
        ShouldBeProblem(result, status).Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Cambio_temporal_solo_permite_Doctor_y_su_paso_en_esta_accion()
    {
        var controller = typeof(AuthController);
        var action = controller.GetMethod(nameof(AuthController.ChangeTemporaryPassword))!;
        action.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe("change-temporary-password");
        controller.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/auth");
        action.GetCustomAttribute<AuthorizeAttribute>()!.Roles.ShouldBe(nameof(UserRole.Doctor));
        action.GetCustomAttribute<AllowPendingStepAttribute>()!.Step.ShouldBe(PendingStep.ChangeTemporaryPassword);
        action.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        controller.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        controller.GetCustomAttribute<AllowPendingStepAttribute>().ShouldBeNull();
        ShouldForbidStoringResponse(nameof(AuthController.ChangeTemporaryPassword));
    }

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
