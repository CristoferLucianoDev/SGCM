using FluentValidation.TestHelper;
using SGCM.Data.Application.Identity.Commands.Login;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class LoginCommandValidatorTests
{
    private const string ValidEmail = "paciente@correo.com";
    private const string ValidPassword = "Secreta123";

    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_HasNoErrors()
    {
        var result = _validator.TestValidate(Command(ValidEmail, ValidPassword));

        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_InvalidEmail_FailsWithInvalidEmail()
    {
        var result = _validator.TestValidate(Command("no-es-un-correo", ValidPassword));

        result.ShouldHaveValidationErrorFor(command => command.Email)
            .WithErrorCode(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public void Validate_PasswordLongerThanMaximum_FailsWithPasswordTooLong()
    {
        var password = new string('a', Password.MaxLength + 1);

        var result = _validator.TestValidate(Command(ValidEmail, password));

        result.ShouldHaveValidationErrorFor(command => command.Password)
            .WithErrorCode(UserErrors.PasswordTooLong.Code);
    }

    // Una contraseña vacía es una credencial inválida más; la rechaza el handler.
    [Fact]
    public void Validate_EmptyPassword_HasNoErrors()
    {
        var result = _validator.TestValidate(Command(ValidEmail, string.Empty));

        result.ShouldNotHaveAnyValidationErrors();
    }

    // La API no exige los campos por su tipo, así que la contraseña puede llegar null.
    [Fact]
    public void Validate_NullPassword_HasNoErrors()
    {
        var result = _validator.TestValidate(Command(ValidEmail, null!));

        result.ShouldNotHaveAnyValidationErrors();
    }

    private static LoginCommand Command(string email, string password) => new(email, password, null, null);
}
