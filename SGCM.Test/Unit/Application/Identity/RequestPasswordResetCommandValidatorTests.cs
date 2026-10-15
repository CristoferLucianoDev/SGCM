using FluentValidation.TestHelper;
using SGCM.Data.Application.Identity.Commands.RequestPasswordReset;
using SGCM.Data.Domain.Common;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class RequestPasswordResetCommandValidatorTests
{
    private readonly RequestPasswordResetCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidEmail_HasNoErrors()
    {
        var result = _validator.TestValidate(new RequestPasswordResetCommand("paciente@correo.com"));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-correo")]
    public void Validate_InvalidEmail_FailsWithInvalidEmail(string email)
    {
        var result = _validator.TestValidate(new RequestPasswordResetCommand(email));

        result.ShouldHaveValidationErrorFor(command => command.Email)
            .WithErrorCode(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public void Validate_EmailLongerThanMaximum_FailsWithInvalidEmail()
    {
        var email = new string('a', 246) + "@correo.com";

        var result = _validator.TestValidate(new RequestPasswordResetCommand(email));

        result.ShouldHaveValidationErrorFor(command => command.Email)
            .WithErrorCode(CommonErrors.InvalidEmail.Code);
    }
}
