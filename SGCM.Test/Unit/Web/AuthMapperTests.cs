using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using SGCM.Web.Mappers.Identity;

namespace SGCM.Test.Unit.Web;

public sealed class AuthMapperTests
{
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

    private static SessionTokens Tokens(PendingStep? step) => new(
        new AccessToken("access-token", AccessTokenExpiresAt),
        "refresh-token",
        RefreshTokenExpiresAt,
        step);
}
