using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Application.Identity.Commands.Logout;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Mappers.Identity;
using System.Text.Json;

namespace SGCM.Test.Unit.Web;

public sealed class AuthMapperTests
{
    [Theory]
    [InlineData(true, true, PendingStep.ChangeTemporaryPassword, true)]
    [InlineData(true, true, PendingStep.DoctorProfile, true)]
    [InlineData(false, true, PendingStep.ChangeTemporaryPassword, false)]
    [InlineData(false, true, PendingStep.DoctorProfile, false)]
    [InlineData(true, false, PendingStep.ChangeTemporaryPassword, false)]
    [InlineData(true, false, PendingStep.PatientProfile, false)]
    [InlineData(true, false, null, false)]
    public void Aviso_web_combina_cliente_y_dato_factual_sin_alterar_tokens(
        bool mobile, bool doctorPending, PendingStep? step, bool expected)
    {
        var tokens = Tokens(step);
        var result = LoginResult.Authenticated(tokens, doctorPending);
        var response = result.ToResponse(mobile);
        response.RequiresWebFirstAccess.ShouldBe(expected);
        response.RequiresTwoFactor.ShouldBeFalse();
        response.AccessToken.ShouldBe(tokens.AccessToken.Value);
        response.AccessTokenExpiresAt.ShouldBe(tokens.AccessToken.ExpiresAt);
        response.RefreshToken.ShouldBe(tokens.RefreshToken);
        response.RefreshTokenExpiresAt.ShouldBe(tokens.RefreshTokenExpiresAt);
        response.PendingStep.ShouldBe(step is { } value ? PendingStepClaimValues.From(value) : null);
        result.ToResponse().RequiresWebFirstAccess.ShouldBeFalse();
    }

    [Fact]
    public void Aviso_web_no_se_anticipa_al_segundo_factor()
    {
        var response = LoginResult.TwoFactorRequired().ToResponse(isMobile: true);
        response.RequiresTwoFactor.ShouldBeTrue();
        response.RequiresWebFirstAccess.ShouldBeFalse();
        response.AccessToken.ShouldBeNull();
        response.RefreshToken.ShouldBeNull();
        response.PendingStep.ShouldBeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Aviso_web_se_serializa_solo_si_es_true_y_conserva_el_contrato_anterior(bool notice)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var response = LoginResult.Authenticated(Tokens(PendingStep.DoctorProfile), hasPendingDoctorFirstAccess: true)
            .ToResponse(isMobile: notice);
        var json = JsonSerializer.SerializeToElement(response, options);
        var legacy = Tokens(PendingStep.DoctorProfile).ToResponse();
        var legacyJson = JsonSerializer.SerializeToElement(legacy, options);
        string[] oldFields = ["requiresTwoFactor", "accessToken", "accessTokenExpiresAt", "refreshToken", "refreshTokenExpiresAt", "pendingStep"];
        legacyJson.EnumerateObject().Select(p => p.Name).Order().ShouldBe(oldFields.Order());
        json.EnumerateObject().Select(p => p.Name).Order()
            .ShouldBe((notice ? oldFields.Append("requiresWebFirstAccess") : oldFields).Order());
        foreach (var name in oldFields) json.GetProperty(name).GetRawText().ShouldBe(legacyJson.GetProperty(name).GetRawText());
        if (notice) json.GetProperty("requiresWebFirstAccess").GetBoolean().ShouldBeTrue();
        else JsonSerializer.Deserialize<LoginResponse>(json, options)!.RequiresWebFirstAccess.ShouldBeFalse();
        new LoginResponse(false, null, null, null, null, null).RequiresWebFirstAccess.ShouldBeFalse();
    }

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

    [Fact]
    public void Cambio_de_contrasena_mapea_ambas_contrasenas_sin_normalizar_y_oculta_los_secretos()
    {
        const string current = "  ClaveActual123  ";
        const string password = "  ClaveNueva123  ";
        var request = new ChangePasswordRequest(current, password);

        var command = request.ToCommand();

        command.CurrentPassword.ShouldBe(current);
        command.NewPassword.ShouldBe(password);
        request.ToString().ShouldNotContain(current);
        request.ToString().ShouldNotContain(password);
        command.ToString().ShouldNotContain(current);
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
