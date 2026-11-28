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

    [Fact]
    public void FindUnknown_devuelve_en_orden_y_sin_repetir_lo_que_no_esta_permitido()
    {
        var unknown = TemplatePlaceholders.FindUnknown(
            "Hola {Name}, su {Fecha} con {Name} y {Otra}.", ["Name"]);

        unknown.ShouldBe(["Fecha", "Otra"]);
    }

    [Theory]
    [InlineData("{}", "")]
    [InlineData("{Con espacio}", "Con espacio")]
    [InlineData("{Nombre1}", "Nombre1")]
    public void FindUnknown_detecta_cualquier_contenido_entre_llaves_como_la_base(string template, string expected)
    {
        TemplatePlaceholders.FindUnknown(template, ["Name"]).ShouldBe([expected]);
    }

    [Fact]
    public void FindUnknown_sin_variables_desconocidas_devuelve_una_lista_vacia()
    {
        TemplatePlaceholders.FindUnknown("Hola {Name}, gracias.", ["Name"]).ShouldBeEmpty();
    }
}
