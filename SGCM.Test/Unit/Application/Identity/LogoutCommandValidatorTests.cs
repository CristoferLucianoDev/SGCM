using FluentValidation.TestHelper;
using SGCM.Data.Application.Identity.Commands.Logout;
using SGCM.Data.Application.Identity.Commands.RefreshSession;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Test.Unit.Application.Identity;

/// <summary>
/// Pruebas de las reglas de <see cref="LogoutCommandValidator"/>.
/// </summary>
public sealed class LogoutCommandValidatorTests
{
    private readonly LogoutCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(new LogoutCommand("refresh-token"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // La API no exige los campos por su tipo, así que el token puede llegar null.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_MissingToken_FailsWithRefreshTokenRequired(string? refreshToken)
    {
        var result = _validator.TestValidate(new LogoutCommand(refreshToken!));

        result.ShouldHaveValidationErrorFor(command => command.RefreshToken)
            .WithErrorCode(UserErrors.RefreshTokenRequired.Code);
    }

    [Fact]
    public void Validate_TokenLongerThanMaximum_FailsWithRefreshTokenRequired()
    {
        var refreshToken = new string('a', RefreshSessionCommandValidator.MaxRefreshTokenLength + 1);

        var result = _validator.TestValidate(new LogoutCommand(refreshToken));

        result.ShouldHaveValidationErrorFor(command => command.RefreshToken)
            .WithErrorCode(UserErrors.RefreshTokenRequired.Code);
    }

    [Fact]
    public void Validate_TokenAtMaximumLength_HasNoErrors()
    {
        var refreshToken = new string('a', RefreshSessionCommandValidator.MaxRefreshTokenLength);

        var result = _validator.TestValidate(new LogoutCommand(refreshToken));

        result.ShouldNotHaveAnyValidationErrors();
    }
}
