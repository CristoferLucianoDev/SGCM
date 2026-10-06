using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas de la política de longitud y del enmascarado del objeto de valor <see cref="Password"/>.
/// </summary>
public class PasswordTests
{
    [Theory]
    [InlineData("12345678")]
    [InlineData("Contrasena-Larga-2026")]
    public void Create_con_8_o_mas_caracteres_devuelve_la_contrasena(string value)
    {
        var result = Password.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234567")]
    public void Create_con_menos_de_8_caracteres_falla(string? value)
    {
        var result = Password.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.PasswordTooShort);
    }

    [Fact]
    public void Create_con_128_caracteres_es_valido()
    {
        var result = Password.Create(new string('a', Password.MaxLength));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_con_mas_de_128_caracteres_falla()
    {
        var result = Password.Create(new string('a', Password.MaxLength + 1));

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.PasswordTooLong);
    }

    [Fact]
    public void Create_no_recorta_espacios()
    {
        var result = Password.Create("  1234  ");

        result.Value.Value.ShouldBe("  1234  ");
    }

    [Fact]
    public void ToString_no_expone_la_contrasena()
    {
        var password = Password.Create("secreta-123").Value;

        password.ToString().ShouldNotContain("secreta-123");
    }
}
