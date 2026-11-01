using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class SpecialtyMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Request_usa_activas_y_la_primera_pagina_de_20_por_defecto()
    {
        var request = new ListSpecialtiesRequest();

        request.Search.ShouldBeNull();
        request.Status.ShouldBe(SpecialtyStatusFilter.Active);
        request.PageNumber.ShouldBe(1);
        request.PageSize.ShouldBe(20);
        request.ToQuery().ShouldBe(new ListSpecialtiesQuery());
    }

    [Theory]
    [InlineData(SpecialtyStatusFilter.Active, true)]
    [InlineData(SpecialtyStatusFilter.Inactive, false)]
    [InlineData(SpecialtyStatusFilter.All, null)]
    public void ToQuery_convierte_el_estado_y_conserva_la_busqueda_y_la_paginacion(
        SpecialtyStatusFilter status, bool? expectedState)
    {
        var request = new ListSpecialtiesRequest("  GINÉCO%_  ", 3, 7, status);

        request.ToQuery().ShouldBe(new ListSpecialtiesQuery("  GINÉCO%_  ", expectedState, 3, 7));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    public void ToQuery_no_convierte_estados_no_definidos_en_un_filtro_valido(int status)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new ListSpecialtiesRequest(Status: (SpecialtyStatusFilter)status).ToQuery());

        exception.ParamName.ShouldBe(nameof(ListSpecialtiesRequest.Status));
    }

    [Theory]
    [InlineData("Active", SpecialtyStatusFilter.Active)]
    [InlineData("inactive", SpecialtyStatusFilter.Inactive)]
    [InlineData("ALL", SpecialtyStatusFilter.All)]
    public async Task Binding_acepta_los_nombres_de_estado(string value, SpecialtyStatusFilter expected)
    {
        var binding = CreateStatusBinding(value);

        await CreateStatusBinder().BindModelAsync(binding);

        binding.ModelState.ErrorCount.ShouldBe(0);
        binding.Result.IsModelSet.ShouldBeTrue();
        binding.Result.Model.ShouldBe(expected);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("3")]
    [InlineData("999")]
    [InlineData("2147483647")]
    [InlineData("Unknown")]
    [InlineData("")]
    [InlineData("null")]
    public async Task Binding_rechaza_valores_no_definidos_o_invalidos_sin_aplicar_un_valor_por_defecto(string value)
    {
        var binding = CreateStatusBinding(value);

        await CreateStatusBinder().BindModelAsync(binding);

        binding.ModelState.IsValid.ShouldBeFalse();
        binding.ModelState["status"]!.Errors.ShouldNotBeEmpty();
        binding.Result.IsModelSet.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void ToResponse_copia_los_datos_del_DTO_sin_modificarlos(bool? state)
    {
        var dto = new SpecialtyDto(42, "Ginecología", "Atención médica.", state);

        dto.ToResponse().ShouldBe(new SpecialtyResponse(42, "Ginecología", "Atención médica.", state));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void JSON_omite_el_estado_nulo_y_conserva_false_y_true(bool? state)
    {
        var response = new SpecialtyDto(42, "Ginecología", "Atención médica.", state).ToResponse();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        json.RootElement.GetProperty("id").GetInt32().ShouldBe(42);
        json.RootElement.GetProperty("name").GetString().ShouldBe("Ginecología");
        json.RootElement.GetProperty("description").GetString().ShouldBe("Atención médica.");
        json.RootElement.TryGetProperty("isActive", out var active).ShouldBe(state.HasValue);
        if (state.HasValue)
        {
            active.GetBoolean().ShouldBe(state.Value);
        }
    }

    [Fact]
    public void ToResponse_conserva_el_orden_los_elementos_y_los_metadatos_de_la_pagina()
    {
        var page = new PagedResult<SpecialtyDto>(
            [new(9, "Zeta", "Primera.", false), new(2, "Alfa", "Segunda.", null)], 3, 2, 7);

        var response = page.ToResponse();

        response.Items.ShouldBe(new[]
        {
            new SpecialtyResponse(9, "Zeta", "Primera.", false),
            new SpecialtyResponse(2, "Alfa", "Segunda.", null)
        });
        response.PageNumber.ShouldBe(3);
        response.PageSize.ShouldBe(2);
        response.TotalCount.ShouldBe(7);
        response.Message.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, "No existen especialidades disponibles")]
    [InlineData(4, null)]
    public void ToResponse_y_JSON_incluyen_el_mensaje_solo_si_el_total_es_cero(int totalCount, string? message)
    {
        var response = new PagedResult<SpecialtyDto>([], 3, 20, totalCount).ToResponse();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        response.Items.ShouldBeEmpty();
        response.PageNumber.ShouldBe(3);
        response.PageSize.ShouldBe(20);
        response.TotalCount.ShouldBe(totalCount);
        response.Message.ShouldBe(message);
        json.RootElement.GetProperty("items").GetArrayLength().ShouldBe(0);
        json.RootElement.GetProperty("totalCount").GetInt32().ShouldBe(totalCount);
        json.RootElement.TryGetProperty("message", out var serializedMessage).ShouldBe(message is not null);
        if (message is not null)
        {
            serializedMessage.GetString().ShouldBe(message);
        }
    }

    private static EnumTypeModelBinder CreateStatusBinder() =>
        new(true, typeof(SpecialtyStatusFilter), NullLoggerFactory.Instance);

    private static ModelBindingContext CreateStatusBinding(string value)
    {
        var values = new QueryCollection(new Dictionary<string, StringValues> { ["status"] = value });
        var provider = new QueryStringValueProvider(BindingSource.Query, values, CultureInfo.InvariantCulture);
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(SpecialtyStatusFilter));

        return DefaultModelBindingContext.CreateBindingContext(
            new ActionContext { HttpContext = new DefaultHttpContext() }, provider, metadata, null, "status");
    }
}
