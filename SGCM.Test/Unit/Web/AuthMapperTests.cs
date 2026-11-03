using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Application.Identity.Commands.Logout;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Mappers.Identity;

namespace SGCM.Test.Unit.Web;

public sealed class AuthMapperTests
{
    [Fact]
    public void Cambio_temporal_mapea_solo_la_contrasena_y_oculta_los_secretos()
    {
        const string password = "  NuevaClave123  ";
        var request = new ChangeTemporaryPasswordRequest(password);
        var command = request.ToCommand();
        command.NewPassword.ShouldBe(password);
        command.GetType().GetProperties().Select(p => p.Name).ShouldBe(["NewPassword"]);
        request.GetType().GetProperties().Select(p => p.Name).ShouldBe(["NewPassword"]);
        request.ToString().ShouldNotContain(password);
        command.ToString().ShouldNotContain(password);
    }

    private static readonly DateTimeOffset AccessTokenExpiresAt = UserMother.Now.AddMinutes(15);
    private static readonly DateTimeOffset RefreshTokenExpiresAt = UserMother.Now.AddDays(7);

    [Fact]
    public void ToResponse_AuthenticatedResult_CopiesTokensExpirationsAndStep()
    {
        var result = LoginResult.Authenticated(Tokens(PendingStep.PatientProfile));

        var response = result.ToResponse();

        response.RequiresTwoFactor.ShouldBeFalse();
        response.AccessToken.ShouldBe("access-token");
        response.AccessTokenExpiresAt.ShouldBe(AccessTokenExpiresAt);
        response.RefreshToken.ShouldBe("refresh-token");
        response.RefreshTokenExpiresAt.ShouldBe(RefreshTokenExpiresAt);
        response.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
    }

    [Fact]
    public void ToResponse_AuthenticatedResultWithoutStep_LeavesPendingStepNull()
    {
        var response = LoginResult.Authenticated(Tokens(null)).ToResponse();

        response.PendingStep.ShouldBeNull();
    }

    [Fact]
    public void ToResponse_TwoFactorRequired_LeavesEverythingNull()
    {
        var response = LoginResult.TwoFactorRequired().ToResponse();

        response.RequiresTwoFactor.ShouldBeTrue();
        response.AccessToken.ShouldBeNull();
        response.AccessTokenExpiresAt.ShouldBeNull();
        response.RefreshToken.ShouldBeNull();
        response.RefreshTokenExpiresAt.ShouldBeNull();
        response.PendingStep.ShouldBeNull();
    }

    [Fact]
    public void ToResponse_SessionTokens_ReturnsTheSameResponseAsAnAuthenticatedLogin()
    {
        var tokens = Tokens(null);

        var response = tokens.ToResponse();

        response.ShouldBe(LoginResult.Authenticated(tokens).ToResponse());
        response.RequiresTwoFactor.ShouldBeFalse();
        response.AccessToken.ShouldBe("access-token");
        response.PendingStep.ShouldBeNull();
    }

    [Fact]
    public void ToCommand_RefreshSessionRequest_CopiesTheToken()
    {
        var command = new RefreshSessionRequest("refresh-token").ToCommand();

        command.ShouldBe(new RefreshSessionCommand("refresh-token"));
    }

    [Fact]
    public void ToCommand_LogoutRequest_CopiesTheToken()
    {
        var command = new LogoutRequest("refresh-token").ToCommand();

        command.ShouldBe(new LogoutCommand("refresh-token"));
    }

    [Fact]
    public void ToRefreshSessionResponse_WithStep_CopiesTokensExpirationsAndStep()
    {
        var response = Tokens(PendingStep.PatientProfile).ToRefreshSessionResponse();

        response.AccessToken.ShouldBe("access-token");
        response.AccessTokenExpiresAt.ShouldBe(AccessTokenExpiresAt);
        response.RefreshToken.ShouldBe("refresh-token");
        response.RefreshTokenExpiresAt.ShouldBe(RefreshTokenExpiresAt);
        response.PendingStep.ShouldBe(PendingStepClaimValues.PatientProfile);
    }

    [Fact]
    public void ToRefreshSessionResponse_WithoutStep_LeavesPendingStepNull()
    {
        var response = Tokens(null).ToRefreshSessionResponse();

        response.PendingStep.ShouldBeNull();
    }

    [Fact]
    public void ToString_RefreshSessionResponse_HidesTheTokens()
    {
        var text = Tokens(null).ToRefreshSessionResponse().ToString();

        text.ShouldNotContain("access-token");
        text.ShouldNotContain("refresh-token");
    }

    private static SessionTokens Tokens(PendingStep? step) => new(
        new AccessToken("access-token", AccessTokenExpiresAt),
        "refresh-token",
        RefreshTokenExpiresAt,
        step);
}
