using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure.Security;

public class TemporaryPasswordGeneratorTests
{
    private const int Samples = 1000;
    private const string AmbiguousCharacters = "0Oo1lI";

    private readonly TemporaryPasswordGenerator _generator = new();

    [Fact]
    public void Generate_devuelve_la_longitud_esperada()
    {
        _generator.Generate().Length.ShouldBe(TemporaryPasswordGenerator.Length);
    }

    [Fact]
    public void Generate_incluye_mayuscula_minuscula_digito_y_simbolo()
    {
        for (var i = 0; i < Samples; i++)
        {
            var password = _generator.Generate();

            password.ShouldContain(c => TemporaryPasswordGenerator.Uppercase.Contains(c));
            password.ShouldContain(c => TemporaryPasswordGenerator.Lowercase.Contains(c));
            password.ShouldContain(c => TemporaryPasswordGenerator.Digits.Contains(c));
            password.ShouldContain(c => TemporaryPasswordGenerator.Symbols.Contains(c));
        }
    }

    [Fact]
    public void Generate_no_usa_caracteres_ambiguos()
    {
        for (var i = 0; i < Samples; i++)
        {
            _generator.Generate().ShouldNotContain(c => AmbiguousCharacters.Contains(c));
        }
    }

    [Fact]
    public void Generate_cumple_la_politica_de_contrasenas()
    {
        Password.Create(_generator.Generate()).IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Generate_no_repite_contrasenas()
    {
        var passwords = Enumerable.Range(0, Samples).Select(_ => _generator.Generate()).ToHashSet();

        passwords.Count.ShouldBe(Samples);
    }
}