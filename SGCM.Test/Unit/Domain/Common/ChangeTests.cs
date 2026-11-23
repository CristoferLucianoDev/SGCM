using SGCM.Data.Domain.Common;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common;

public sealed class ChangeTests
{
    [Fact]
    public void El_valor_por_defecto_no_esta_enviado_y_no_tiene_valor()
    {
        Change<string?> change = default;

        change.IsProvided.ShouldBeFalse();
        change.Value.ShouldBeNull();
    }

    [Fact]
    public void Set_con_un_valor_lo_marca_como_enviado_con_ese_valor()
    {
        var change = Change<string?>.Set("nuevo");

        change.IsProvided.ShouldBeTrue();
        change.Value.ShouldBe("nuevo");
    }

    [Fact]
    public void Set_con_nulo_esta_enviado_con_valor_nulo()
    {
        var change = Change<string?>.Set(null);

        change.IsProvided.ShouldBeTrue();
        change.Value.ShouldBeNull();
    }

    [Fact]
    public void Unchanged_equivale_al_valor_por_defecto()
    {
        Change<string?>.Unchanged.ShouldBe(default);
        Change<string?>.Unchanged.IsProvided.ShouldBeFalse();
    }

    [Fact]
    public void Enviar_nulo_es_distinto_de_no_enviar()
    {
        Change<string?>.Set(null).ShouldNotBe(Change<string?>.Unchanged);
    }

    [Fact]
    public void Dos_cambios_con_el_mismo_estado_y_valor_son_iguales()
    {
        Change<string?>.Set("x").ShouldBe(Change<string?>.Set("x"));
        Change<string?>.Set("x").ShouldNotBe(Change<string?>.Set("y"));
    }

    [Fact]
    public void Funciona_con_tipos_por_valor_y_distingue_enviar_el_valor_por_defecto()
    {
        Change<int>.Set(0).IsProvided.ShouldBeTrue();
        Change<int>.Set(0).Value.ShouldBe(0);
        Change<int>.Unchanged.IsProvided.ShouldBeFalse();
    }
}
