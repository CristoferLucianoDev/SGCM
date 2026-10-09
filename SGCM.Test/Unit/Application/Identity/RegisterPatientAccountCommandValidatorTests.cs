using FluentValidation.TestHelper;
using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class RegisterPatientAccountCommandValidatorTests
{
    private const string ValidEmail = "paciente@correo.com";
    private const string ValidPassword = "Secreta123";

    private readonly RegisterPatientAccountCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(new RegisterPatientAccountCommand(ValidEmail, ValidPassword));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-es-un-correo")]
    public void Validate_InvalidEmail_FailsWithInvalidEmail(string email)
    {
        var result = _validator.TestValidate(new RegisterPatientAccountCommand(email, ValidPassword));

        result.ShouldHaveValidationErrorFor(command => command.Email)
            .WithErrorCode(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public void Validate_EmailLongerThanMaximum_FailsWithInvalidEmail()
    {
        var email = new string('a', 246) + "@correo.com";

        var result = _validator.TestValidate(new RegisterPatientAccountCommand(email, ValidPassword));

        result.ShouldHaveValidationErrorFor(command => command.Email)
            .WithErrorCode(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public void Validate_PasswordShorterThanMinimum_FailsWithPasswordTooShort()
    {
        var password = new string('a', Password.MinLength - 1);

        var result = _validator.TestValidate(new RegisterPatientAccountCommand(ValidEmail, password));

        result.ShouldHaveValidationErrorFor(command => command.Password)
            .WithErrorCode(UserErrors.PasswordTooShort.Code);
    }

    [Fact]
    public void Validate_PasswordLongerThanMaximum_FailsWithPasswordTooLong()
    {
        var password = new string('a', Password.MaxLength + 1);

        var result = _validator.TestValidate(new RegisterPatientAccountCommand(ValidEmail, password));

        result.ShouldHaveValidationErrorFor(command => command.Password)
            .WithErrorCode(UserErrors.PasswordTooLong.Code);
    }

    [Fact]
    public void ToString_DoesNotExposePassword()
    {
        var command = new RegisterPatientAccountCommand(ValidEmail, ValidPassword);

        command.ToString().ShouldNotContain(ValidPassword);
    }
}