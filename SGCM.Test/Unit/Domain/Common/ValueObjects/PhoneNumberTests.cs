using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common.ValueObjects;

public sealed class PhoneNumberTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t\r\n ")]
    public void Create_rechaza_un_telefono_ausente(string? value)
    {
        var result = PhoneNumber.Create(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommonErrors.PhoneNumberRequired);
    }

    [Theory]
    [InlineData(" 1 ", "1")]
    [InlineData("\t12345678901234567890 ", "12345678901234567890")]
    [InlineData(" 809-555-0100 ", "809-555-0100")]
    [InlineData("8095550200", "8095550200")]
    [InlineData("  (809) 555-0100 x2\t", "(809) 555-0100 x2")]
    public void Create_recorta_los_extremos_y_conserva_el_contenido_interno(string value, string expected)
    {
        var result = PhoneNumber.Create(value);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(expected);
    }

    [Fact]
    public void Create_rechaza_21_caracteres_despues_de_recortar()
    {
        var result = PhoneNumber.Create(" " + new string('1', 21) + " ");

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommonErrors.PhoneNumberTooLong);
    }

    [Fact]
    public void La_igualdad_depende_del_valor_recortado()
    {
        var number = PhoneNumber.Create("809-555-0100").Value;
        var equivalent = PhoneNumber.Create(" 809-555-0100 ").Value;

        number.ShouldBe(equivalent);
        number.GetHashCode().ShouldBe(equivalent.GetHashCode());
        number.ShouldNotBe(PhoneNumber.Create("8095550100").Value);
    }
}
