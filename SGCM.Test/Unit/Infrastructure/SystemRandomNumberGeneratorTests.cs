using SGCM.Data.Infrastructure.Services;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class SystemRandomNumberGeneratorTests
{
    private readonly SystemRandomNumberGenerator _random = new();

    [Fact]
    public void Con_limite_uno_siempre_devuelve_cero()
    {
        Enumerable.Range(0, 20).Select(_ => _random.Next(1)).ShouldAllBe(value => value == 0);
    }

    [Fact]
    public void Devuelve_valores_dentro_del_rango_con_el_limite_excluido()
    {
        Enumerable.Range(0, 200).Select(_ => _random.Next(3)).ShouldAllBe(value => value >= 0 && value < 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_limite_no_positivo_es_un_error_de_programacion(int maxExclusive)
    {
        Should.Throw<ArgumentOutOfRangeException>(() => _random.Next(maxExclusive));
    }
}
