using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class NotificationTemplateStoreTests
{
    private readonly NotificationTemplateStore _store = new(
        typeof(NotificationTemplateStoreTests).Assembly,
        "SGCM.Test.Unit.Infrastructure.Templates.");

    [Fact]
    public void Get_toma_el_asunto_de_la_etiqueta_title()
    {
        var template = _store.Get("test-template");

        template.Subject.ShouldBe("Asunto de prueba");
    }

    [Fact]
    public void Get_inserta_el_cuerpo_en_el_layout_sin_la_etiqueta_title()
    {
        var template = _store.Get("test-template");

        template.HtmlBody.ShouldContain("<body>");
        template.HtmlBody.ShouldContain("<p>Hola {Name}</p>");
        template.HtmlBody.ShouldNotContain("<title>");
        template.HtmlBody.ShouldNotContain("{Content}");
    }

    [Fact]
    public void Get_de_una_plantilla_inexistente_lanza_excepcion()
    {
        Should.Throw<InvalidOperationException>(() => _store.Get("no-existe"));
    }

    [Fact]
    public void Get_de_una_plantilla_sin_asunto_lanza_excepcion()
    {
        Should.Throw<InvalidOperationException>(() => _store.Get("no-subject"));
    }

    [Fact]
    public void Get_reutiliza_la_plantilla_ya_cargada()
    {
        var first = _store.Get("test-template");
        var second = _store.Get("test-template");

        second.ShouldBeSameAs(first);
    }
}