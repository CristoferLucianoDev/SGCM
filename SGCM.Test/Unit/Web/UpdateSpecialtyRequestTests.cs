using System.Text.Json;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class UpdateSpecialtyRequestTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("{}", false, null, false, null)]
    [InlineData("{\"name\":null}", true, null, false, null)]
    [InlineData("{\"description\":null}", false, null, true, null)]
    [InlineData("{\"name\":\"\"}", true, "", false, null)]
    [InlineData("{\"description\":\"\"}", false, null, true, "")]
    [InlineData("{\"name\":\"Neurología\"}", true, "Neurología", false, null)]
    [InlineData("{\"description\":\"Nueva descripción\"}", false, null, true, "Nueva descripción")]
    [InlineData("{\"name\":\"Neurología\",\"description\":\"Nueva descripción\"}", true, "Neurología", true, "Nueva descripción")]
    [InlineData("{\"NAME\":\"Neurología\",\"DeScRiPtIoN\":null}", true, "Neurología", true, null)]
    [InlineData("{\"Name\":null,\"Description\":\"  Descripción  \"}", true, null, true, "  Descripción  ")]
    public void JSON_distingue_omision_null_y_valor_sin_depender_de_la_capitalizacion(
        string json, bool nameProvided, string? name, bool descriptionProvided, string? description)
    {
        var request = JsonSerializer.Deserialize<UpdateSpecialtyRequest>(json, JsonOptions)!;

        request.NameProvided.ShouldBe(nameProvided);
        request.Name.ShouldBe(name);
        request.DescriptionProvided.ShouldBe(descriptionProvided);
        request.Description.ShouldBe(description);
    }

    [Theory]
    [InlineData("{\"nameProvided\":true,\"descriptionProvided\":true}", false, false)]
    [InlineData("{\"NameProvided\":true,\"DESCRIPTIONPROVIDED\":true}", false, false)]
    [InlineData("{\"name\":null,\"nameProvided\":false,\"descriptionProvided\":true}", true, false)]
    [InlineData("{\"descriptionProvided\":false,\"description\":null,\"nameProvided\":true}", false, true)]
    public void JSON_no_permite_manipular_los_indicadores(string json, bool nameProvided, bool descriptionProvided)
    {
        var request = JsonSerializer.Deserialize<UpdateSpecialtyRequest>(json, JsonOptions)!;

        request.NameProvided.ShouldBe(nameProvided);
        request.DescriptionProvided.ShouldBe(descriptionProvided);
        request.Name.ShouldBeNull();
        request.Description.ShouldBeNull();
    }

    [Theory]
    [InlineData("{\"name\":42}")]
    [InlineData("{\"name\":false}")]
    [InlineData("{\"description\":[]}")]
    [InlineData("{\"description\":{}}")]
    public void JSON_rechaza_tipos_incompatibles(string json)
    {
        Should.Throw<JsonException>(() => JsonSerializer.Deserialize<UpdateSpecialtyRequest>(json, JsonOptions));
    }

    [Fact]
    public void JSON_no_serializa_los_indicadores()
    {
        var request = new UpdateSpecialtyRequest { Name = null, Description = "Descripción." };

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(request, JsonOptions));

        json.RootElement.TryGetProperty("nameProvided", out _).ShouldBeFalse();
        json.RootElement.TryGetProperty("descriptionProvided", out _).ShouldBeFalse();
        request.NameProvided.ShouldBeTrue();
        request.DescriptionProvided.ShouldBeTrue();
    }
}
