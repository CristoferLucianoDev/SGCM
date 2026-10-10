using FluentValidation.TestHelper;
using SGCM.Data.Application.Identity.Commands.ConfirmEmail;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ConfirmEmailCommandValidatorTests
{
    private const int MaxTokenLength = 256;

    private static readonly string ValidToken = new('a', 43);

    private readonly ConfirmEmailCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidToken_HasNoErrors()
    {
        var result = _validator.TestValidate(new ConfirmEmailCommand(ValidToken));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankToken_FailsWithTokenInvalid(string token)
    {
        var result = _validator.TestValidate(new ConfirmEmailCommand(token));

        result.ShouldHaveValidationErrorFor(command => command.Token)
            .WithErrorCode(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public void Validate_TokenLongerThanMaximum_FailsWithTokenInvalid()
    {
        var token = new string('a', MaxTokenLength + 1);

        var result = _validator.TestValidate(new ConfirmEmailCommand(token));

        result.ShouldHaveValidationErrorFor(command => command.Token)
            .WithErrorCode(UserErrors.TokenInvalid.Code);
    }

    [Fact]
    public void ToString_DoesNotExposeToken()
    {
        var command = new ConfirmEmailCommand(ValidToken);

        command.ToString().ShouldNotContain(ValidToken);
    }
}
