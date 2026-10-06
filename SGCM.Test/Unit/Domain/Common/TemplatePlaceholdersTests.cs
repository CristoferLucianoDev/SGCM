using SGCM.Data.Domain.Common;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common;

public class TemplatePlaceholdersTests
{
    [Fact]
    public void Extract_devuelve_las_variables_sin_repetir()
    {
        var variables = TemplatePlaceholders.Extract("Hola {Name}, tu cita es el {Date}. Gracias, {Name}.");

        variables.ShouldBe(["Name", "Date"]);
    }

    [Fact]
    public void Extract_sin_variables_devuelve_una_lista_vacia()
    {
        TemplatePlaceholders.Extract("Texto sin variables").ShouldBeEmpty();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{Con espacio}")]
    [InlineData("{Nombre1}")]
    public void Extract_ignora_lo_que_no_es_una_variable_valida(string template)
    {
        TemplatePlaceholders.Extract(template).ShouldBeEmpty();
    }
}