using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Binders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;
using SGCM.Data.Domain.Common;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class InsuranceProviderMapperTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("  Seguros Universal  ", " 8095550200 ", "Contacto@Seguros.com", " Av. Winston Churchill 100 ", " https://seguros.com ")]
    [InlineData("Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null)]
    public void ToCommand_registro_copia_los_campos_sin_transformarlos(
        string name, string contactPhone, string contactEmail, string address, string? website)
    {
        var request = new RegisterInsuranceProviderRequest(name, contactPhone, contactEmail, address, website);

        request.ToCommand().ShouldBe(new RegisterInsuranceProviderCommand(name, contactPhone, contactEmail, address, website));
    }

    [Fact]
    public void ToCommand_actualizacion_copia_el_id_y_los_cambios_sin_transformar_los_valores()
    {
        var request = new UpdateInsuranceProviderRequest
        {
            Name = "  Seguros Universal  ",
            ContactEmail = " Contacto@Seguros.com ",
            Website = null
        };

        var command = request.ToCommand(42);

        command.Id.ShouldBe(42);
        command.Changes.Name.ShouldBe(Change<string?>.Set("  Seguros Universal  "));
        command.Changes.ContactEmail.ShouldBe(Change<string?>.Set(" Contacto@Seguros.com "));
        command.Changes.Website.ShouldBe(Change<string?>.Set(null));
        command.Changes.ContactPhone.IsProvided.ShouldBeFalse();
        command.Changes.Address.IsProvided.ShouldBeFalse();
        command.ShouldBe(new UpdateInsuranceProviderCommand(42, request.Changes));
    }

    [Fact]
    public void ToCommand_actualizacion_sin_campos_no_marca_ningun_cambio()
    {
        var command = new UpdateInsuranceProviderRequest().ToCommand(7);

        command.Id.ShouldBe(7);
        command.Changes.HasChanges.ShouldBeFalse();
    }

    [Fact]
    public void Request_de_listado_usa_activos_y_la_primera_pagina_de_20_por_defecto()
    {
        var request = new ListInsuranceProvidersRequest();

        request.Search.ShouldBeNull();
        request.Status.ShouldBe(InsuranceProviderStatusFilter.Active);
        request.PageNumber.ShouldBe(1);
        request.PageSize.ShouldBe(20);
        request.ToQuery().ShouldBe(new ListInsuranceProvidersQuery());
    }

    [Theory]
    [InlineData(InsuranceProviderStatusFilter.Active, true)]
    [InlineData(InsuranceProviderStatusFilter.Inactive, false)]
    [InlineData(InsuranceProviderStatusFilter.All, null)]
    public void ToQuery_convierte_el_estado_y_conserva_la_busqueda_y_la_paginacion(
        InsuranceProviderStatusFilter status, bool? expectedState)
    {
        var request = new ListInsuranceProvidersRequest("  UNIVÉR%_  ", 3, 7, status);

        request.ToQuery().ShouldBe(new ListInsuranceProvidersQuery("  UNIVÉR%_  ", expectedState, 3, 7));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(999)]
    [InlineData(int.MaxValue)]
    public void ToQuery_no_convierte_estados_no_definidos_en_un_filtro_valido(int status)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new ListInsuranceProvidersRequest(Status: (InsuranceProviderStatusFilter)status).ToQuery());

        exception.ParamName.ShouldBe(nameof(ListInsuranceProvidersRequest.Status));
    }

    [Theory]
    [InlineData("Active", InsuranceProviderStatusFilter.Active)]
    [InlineData("inactive", InsuranceProviderStatusFilter.Inactive)]
    [InlineData("ALL", InsuranceProviderStatusFilter.All)]
    public async Task Binding_acepta_los_nombres_de_estado(string value, InsuranceProviderStatusFilter expected)
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
    [InlineData(null, null)]
    [InlineData("https://universal.example", true)]
    [InlineData("https://universal.example", false)]
    public void ToResponse_copia_los_datos_del_DTO_sin_modificarlos(string? website, bool? state)
    {
        var dto = new InsuranceProviderDto(
            42, "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", website, state);

        dto.ToResponse().ShouldBe(new InsuranceProviderResponse(
            42, "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", website, state));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("https://universal.example", false)]
    [InlineData(null, true)]
    public void JSON_omite_el_estado_nulo_conserva_false_y_true_y_serializa_el_sitio_web_nulo(string? website, bool? state)
    {
        var response = new InsuranceProviderDto(
            42, "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", website, state)
            .ToResponse();
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response, JsonOptions));

        json.RootElement.GetProperty("id").GetInt32().ShouldBe(42);
        json.RootElement.GetProperty("name").GetString().ShouldBe("Seguros Universal");
        json.RootElement.GetProperty("contactPhone").GetString().ShouldBe("8095550200");
        json.RootElement.GetProperty("contactEmail").GetString().ShouldBe("contacto@seguros.com");
        json.RootElement.GetProperty("address").GetString().ShouldBe("Av. Winston Churchill 100");
        json.RootElement.GetProperty("website").GetString().ShouldBe(website);
        json.RootElement.TryGetProperty("isActive", out var active).ShouldBe(state.HasValue);
        if (state.HasValue)
        {
            active.GetBoolean().ShouldBe(state.Value);
        }
    }

    [Fact]
    public void ToResponse_de_la_pagina_conserva_el_orden_los_elementos_y_los_metadatos()
    {
        var page = new PagedResult<InsuranceProviderDto>(
            [
                new(9, "Zeta", "8095550001", "zeta@seguros.com", "Calle 1", null, false),
                new(2, "Alfa", "8095550002", "alfa@seguros.com", "Calle 2", "https://alfa.example", null)
            ],
            3, 2, 7);

        var response = page.ToResponse();

        response.Items.ShouldBe(new[]
        {
            new InsuranceProviderResponse(9, "Zeta", "8095550001", "zeta@seguros.com", "Calle 1", null, false),
            new InsuranceProviderResponse(2, "Alfa", "8095550002", "alfa@seguros.com", "Calle 2", "https://alfa.example", null)
        });
        response.PageNumber.ShouldBe(3);
        response.PageSize.ShouldBe(2);
        response.TotalCount.ShouldBe(7);
        response.Message.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, "No existen registros de proveedores de seguros")]
    [InlineData(4, null)]
    public void ToResponse_y_JSON_incluyen_el_mensaje_solo_si_el_total_es_cero(int totalCount, string? message)
    {
        var response = new PagedResult<InsuranceProviderDto>([], 3, 20, totalCount).ToResponse();
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
        new(true, typeof(InsuranceProviderStatusFilter), NullLoggerFactory.Instance);

    private static ModelBindingContext CreateStatusBinding(string value)
    {
        var values = new QueryCollection(new Dictionary<string, StringValues> { ["status"] = value });
        var provider = new QueryStringValueProvider(BindingSource.Query, values, CultureInfo.InvariantCulture);
        var metadata = new EmptyModelMetadataProvider().GetMetadataForType(typeof(InsuranceProviderStatusFilter));

        return DefaultModelBindingContext.CreateBindingContext(
            new ActionContext { HttpContext = new DefaultHttpContext() }, provider, metadata, null, "status");
    }
}
