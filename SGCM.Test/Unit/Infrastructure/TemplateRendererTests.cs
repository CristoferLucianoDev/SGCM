using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Infrastructure.Notifications;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class TemplateRendererTests
{
    private readonly TemplateRenderer _renderer = new();

    [Fact]
    public void Render_reemplaza_las_variables_con_sus_valores()
    {
        var values = new Dictionary<string, string> { ["Name"] = "Ana", ["Date"] = "10/10/2026" };

        var result = _renderer.Render("Hola {Name}, tu cita es el {Date}.", values, htmlEncode: false);

        result.ShouldBe("Hola Ana, tu cita es el 10/10/2026.");
    }

    [Fact]
    public void Render_deja_intactas_las_variables_sin_valor()
    {
        var values = new Dictionary<string, string> { ["Name"] = "Ana" };

        var result = _renderer.Render("Hola {Name}, {Unknown}.", values, htmlEncode: false);

        result.ShouldBe("Hola Ana, {Unknown}.");
    }

    [Fact]
    public void Render_con_htmlEncode_escapa_los_valores()
    {
        var values = new Dictionary<string, string> { ["Name"] = "<script>alert(1)</script>" };

        var result = _renderer.Render("<p>Hola {Name}</p>", values, htmlEncode: true);

        result.ShouldBe("<p>Hola &lt;script&gt;alert(1)&lt;/script&gt;</p>");
    }

    [Fact]
    public void Render_sin_htmlEncode_no_escapa_los_valores()
    {
        var values = new Dictionary<string, string> { ["Name"] = "Ana & Luis" };

        var result = _renderer.Render("Hola {Name}", values, htmlEncode: false);

        result.ShouldBe("Hola Ana & Luis");
    }
}