using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ChangeTemporaryPasswordCommandValidatorTests
{
    [Theory]
    [InlineData(null, "User.PasswordTooShort")]
    [InlineData("", "User.PasswordTooShort")]
    [InlineData("1234567", "User.PasswordTooShort")]
    [InlineData("12345678", null)]
    [InlineData("        ", null)]
    [InlineData("  clave  ", null)]
    public void Reutiliza_la_politica_sin_normalizar(string? password, string? error)
    {
        var command = new ChangeTemporaryPasswordCommand(password!);
        var result = new ChangeTemporaryPasswordCommandValidator().Validate(command);
        result.IsValid.ShouldBe(error is null);
        if (error is not null) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(error);
        command.NewPassword.ShouldBe(password);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Respeta_el_maximo_actual(int length, bool valid)
    {
        var result = new ChangeTemporaryPasswordCommandValidator().Validate(
            new ChangeTemporaryPasswordCommand(new string('x', length)));
        result.IsValid.ShouldBe(valid);
        if (!valid) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("User.PasswordTooLong");
    }
}

