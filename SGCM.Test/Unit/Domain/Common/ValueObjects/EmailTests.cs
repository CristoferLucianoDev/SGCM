using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common.ValueObjects;

/// <summary>
/// Pruebas de validación, normalización e igualdad del objeto de valor <see cref="Email"/>.
/// </summary>
public class EmailTests
{
    [Theory]
    [InlineData("paciente@correo.com")]
    [InlineData("paciente@clinica.org.do")]
    public void Create_con_formato_valido_devuelve_el_correo(string value)
    {
        var result = Email.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(value);
    }

    [Fact]
    public void Create_recorta_espacios_y_pasa_a_minusculas()
    {
        var result = Email.Create("  Paciente@Correo.COM");

        result.Value.Value.ShouldBe("paciente@correo.com");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sin-arroba")]
    [InlineData("@correo.com")]
    [InlineData("paciente@")]
    [InlineData("Paciente <paciente@correo.com>")]
    public void Create_con_valor_invalido_devuelve_error(string? value)
    {
        var result = Email.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommonErrors.InvalidEmail);
    }

    [Fact]
    public void Create_con_256_caracteres_es_valido()
    {
        var value = new string('a', 245) + "@correo.com";

        var result = Email.Create(value);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Create_con_mas_de_256_caracteres_falla()
    {
        var value = new string('a', 246) + "@correo.com";

        var result = Email.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommonErrors.InvalidEmail);
    }

    [Fact]
    public void Dos_correos_con_el_mismo_valor_son_iguales()
    {
        var first = Email.Create("paciente@correo.com").Value;
        var second = Email.Create("PACIENTE@correo.com").Value;

        first.ShouldBe(second);
    }
}
