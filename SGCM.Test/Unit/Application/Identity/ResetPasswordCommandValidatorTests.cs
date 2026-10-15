using FluentValidation.TestHelper;
using SGCM.Data.Application.Identity.Commands.ResetPassword;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ResetPasswordCommandValidatorTests
{
    private const int MaxTokenLength = 256;
    private const string ValidPassword = "NuevaClave123";

    private static readonly string ValidToken = new('a', 43);

    private readonly ResetPasswordCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidTokenAndPassword_HasNoErrors()
    {
        var result = _validator.TestValidate(new ResetPasswordCommand(ValidToken, ValidPassword));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankToken_FailsWithTokenInvalid(string token)
    {
        var result = _validator.TestValidate(new ResetPasswordCommand(token, ValidPassword));

        result.ShouldHaveValidationErrorFor(command => command.Token)
            .WithErrorCode(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public void Validate_TokenLongerThanMaximum_FailsWithTokenInvalid()
    {
        var token = new string('a', MaxTokenLength + 1);

        var result = _validator.TestValidate(new ResetPasswordCommand(token, ValidPassword));

        result.ShouldHaveValidationErrorFor(command => command.Token)
            .WithErrorCode(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public void Validate_PasswordShorterThanMinimum_FailsWithPasswordTooShort()
    {
        var password = new string('a', Password.MinLength - 1);

        var result = _validator.TestValidate(new ResetPasswordCommand(ValidToken, password));

        result.ShouldHaveValidationErrorFor(command => command.NewPassword)
            .WithErrorCode(UserErrors.PasswordTooShort.Code);
    }

    [Fact]
    public void Validate_PasswordLongerThanMaximum_FailsWithPasswordTooLong()
    {
        var password = new string('a', Password.MaxLength + 1);

        var result = _validator.TestValidate(new ResetPasswordCommand(ValidToken, password));

        result.ShouldHaveValidationErrorFor(command => command.NewPassword)
            .WithErrorCode(UserErrors.PasswordTooLong.Code);
    }

    [Fact]
    public void ToString_DoesNotExposeTokenNorPassword()
    {
        var command = new ResetPasswordCommand(ValidToken, ValidPassword);

        command.ToString().ShouldNotContain(ValidToken);
        command.ToString().ShouldNotContain(ValidPassword);
    }
}
