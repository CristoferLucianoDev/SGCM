using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common.ValueObjects;

public sealed class MoneyTests
{
    public static TheoryData<decimal> ValidAmounts => new()
    {
        0m, 12m, 12.34m, 12.340m, 99999999.99m
    };

    public static TheoryData<decimal> InvalidAmounts => new()
    {
        -1m, 12.345m, 99999999.991m, 100000000m, decimal.MinValue, decimal.MaxValue
    };

    [Theory]
    [MemberData(nameof(ValidAmounts))]
    public void Create_acepta_importes_representables_en_centavos(decimal amount)
    {
        var result = Money.Create(amount);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Value.ShouldBe(amount);
    }

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void Create_rechaza_importes_fuera_de_rango_o_con_fracciones_de_centavo(decimal amount)
    {
        var result = Money.Create(amount);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(CommonErrors.InvalidMoney);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void La_igualdad_depende_del_importe_y_admite_ceros_decimales_adicionales()
    {
        var amount = Money.Create(12.34m).Value;
        var equivalent = Money.Create(12.340m).Value;

        amount.ShouldBe(equivalent);
        amount.GetHashCode().ShouldBe(equivalent.GetHashCode());
        amount.ShouldNotBe(Money.Create(12.35m).Value);
    }
}
