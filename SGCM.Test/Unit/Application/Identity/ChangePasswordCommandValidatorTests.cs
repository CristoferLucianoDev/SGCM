using SGCM.Data.Application.Identity.Commands.ChangePassword;
using Shouldly;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class ChangePasswordCommandValidatorTests
{
    private const string CurrentPassword = "ClaveActual123";

    [Theory]
    [InlineData(null, "User.CurrentPasswordRequired")]
    [InlineData("", "User.CurrentPasswordRequired")]
    [InlineData("x", null)]
    public void Exige_la_contraseña_actual(string? current, string? error)
    {
        var result = new ChangePasswordCommandValidator().Validate(new ChangePasswordCommand(current!, "ClaveNueva123"));

        result.IsValid.ShouldBe(error is null);
        if (error is not null) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(error);
    }

    [Theory]
    [InlineData(null, "User.PasswordTooShort")]
    [InlineData("", "User.PasswordTooShort")]
    [InlineData("1234567", "User.PasswordTooShort")]
    [InlineData("12345678", null)]
    [InlineData("  clave  ", null)]
    public void Reutiliza_la_politica_de_contraseña_sin_normalizar(string? password, string? error)
    {
        var command = new ChangePasswordCommand(CurrentPassword, password!);

        var result = new ChangePasswordCommandValidator().Validate(command);

        result.IsValid.ShouldBe(error is null);
        if (error is not null) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(error);
        command.NewPassword.ShouldBe(password);
    }

    [Theory]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Respeta_el_maximo_actual(int length, bool valid)
    {
        var result = new ChangePasswordCommandValidator().Validate(
            new ChangePasswordCommand(CurrentPassword, new string('x', length)));

        result.IsValid.ShouldBe(valid);
        if (!valid) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("User.PasswordTooLong");
    }
}
