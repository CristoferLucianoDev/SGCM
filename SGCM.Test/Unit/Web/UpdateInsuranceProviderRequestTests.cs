using System.Text.Json;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class UpdateInsuranceProviderRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string[] Fields = ["name", "contactPhone", "contactEmail", "address", "website"];

    public static IEnumerable<object?[]> FieldCases()
    {
        foreach (var field in Fields)
        {
            yield return [field, "{}", false, null];
            yield return [field, $"{{\"{field}\":null}}", true, null];
            yield return [field, $"{{\"{field}\":\"\"}}", true, ""];
            yield return [field, $"{{\"{field}\":\"  Valor  \"}}", true, "  Valor  "];
            // El nombre de la propiedad no distingue mayúsculas.
            yield return [field, $"{{\"{field.ToUpperInvariant()}\":\"Valor\"}}", true, "Valor"];
            yield return [field, $"{{\"{char.ToUpperInvariant(field[0])}{field[1..]}\":null}}", true, null];
        }
    }

    [Theory]
    [MemberData(nameof(FieldCases))]
    public void JSON_distingue_omision_null_vacio_y_valor_en_cada_campo_sin_depender_de_la_capitalizacion(
        string field, string json, bool provided, string? value)
    {
        var request = JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>(json, JsonOptions)!;

        var change = ChangeOf(request.Changes, field);
        change.IsProvided.ShouldBe(provided);
        change.Value.ShouldBe(value);
        // Ningún otro campo se marca como enviado.
        Fields.Where(other => other != field).ShouldAllBe(other => !ChangeOf(request.Changes, other).IsProvided);
        request.Changes.HasChanges.ShouldBe(provided);
    }

    [Fact]
    public void JSON_con_objeto_vacio_no_marca_ningun_campo()
    {
        var request = JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>("{}", JsonOptions)!;

        request.Changes.HasChanges.ShouldBeFalse();
        request.Changes.ShouldBe(default);
    }

    [Fact]
    public void JSON_con_todos_los_campos_los_marca_todos_con_su_valor()
    {
        const string json = """
            {"name":"A","contactPhone":"B","contactEmail":"C","address":"D","website":null}
            """;

        var changes = JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>(json, JsonOptions)!.Changes;

        changes.Name.ShouldBe(Change<string?>.Set("A"));
        changes.ContactPhone.ShouldBe(Change<string?>.Set("B"));
        changes.ContactEmail.ShouldBe(Change<string?>.Set("C"));
        changes.Address.ShouldBe(Change<string?>.Set("D"));
        changes.Website.ShouldBe(Change<string?>.Set(null));
    }

    [Theory]
    [InlineData("{\"changes\":{\"name\":{\"isProvided\":true,\"value\":\"Manipulado\"}}}")]
    [InlineData("{\"Changes\":{\"Website\":{\"IsProvided\":true,\"Value\":null}}}")]
    [InlineData("{\"CHANGES\":null}")]
    [InlineData("{\"isProvided\":true,\"nameProvided\":true,\"hasChanges\":true}")]
    public void JSON_no_permite_manipular_los_indicadores_ni_Changes(string json)
    {
        var request = JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>(json, JsonOptions)!;

        request.Changes.HasChanges.ShouldBeFalse();
        request.Name.ShouldBeNull();
        request.Website.ShouldBeNull();
    }

    [Fact]
    public void JSON_con_Changes_y_un_campo_real_solo_respeta_el_campo_real()
    {
        const string json = """
            {"name":"Real","changes":{"address":{"isProvided":true,"value":"Falso"}}}
            """;

        var changes = JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>(json, JsonOptions)!.Changes;

        changes.Name.ShouldBe(Change<string?>.Set("Real"));
        changes.Address.IsProvided.ShouldBeFalse();
    }

    [Theory]
    [InlineData("{\"name\":42}")]
    [InlineData("{\"contactPhone\":false}")]
    [InlineData("{\"contactEmail\":[]}")]
    [InlineData("{\"address\":{}}")]
    [InlineData("{\"website\":1.5}")]
    public void JSON_rechaza_tipos_incompatibles(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>(json, JsonOptions));
    }

    [Fact]
    public void JSON_no_serializa_los_indicadores_ni_Changes()
    {
        var request = new UpdateInsuranceProviderRequest { Name = "Seguros", Website = null };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));

        json.RootElement.EnumerateObject().Select(property => property.Name)
            .ShouldBe(new[] { "name", "contactPhone", "contactEmail", "address", "website" }, ignoreOrder: true);
        json.RootElement.TryGetProperty("changes", out _).ShouldBeFalse();
        request.Changes.Name.IsProvided.ShouldBeTrue();
        request.Changes.Website.IsProvided.ShouldBeTrue();
        request.Changes.Address.IsProvided.ShouldBeFalse();
    }

    private static Change<string?> ChangeOf(InsuranceProviderChanges changes, string field) => field switch
    {
        "name" => changes.Name,
        "contactPhone" => changes.ContactPhone,
        "contactEmail" => changes.ContactEmail,
        "address" => changes.Address,
        "website" => changes.Website,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };
}
