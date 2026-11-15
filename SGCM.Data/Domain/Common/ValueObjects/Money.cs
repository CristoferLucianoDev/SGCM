namespace SGCM.Data.Domain.Common.ValueObjects;

/// <summary>Importe no negativo representable en centavos.</summary>
public sealed record Money
{
    public const decimal MaxAmount = 99999999.99m;

    public decimal Value { get; }

    private Money(decimal value)
    {
        Value = value;
    }

    public static Result<Money> Create(decimal value)
    {
        if (value < 0m || value > MaxAmount || value != decimal.Round(value, 2))
        {
            return CommonErrors.InvalidMoney;
        }

        return new Money(value);
    }

    // La columna numeric(10,2) y su CHECK garantizan la escala y el rango.
    internal static Money FromStoredValue(decimal value) => new(value);
}
