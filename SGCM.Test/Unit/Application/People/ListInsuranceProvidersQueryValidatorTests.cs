using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class ListInsuranceProvidersQueryValidatorTests
{
    private readonly ListInsuranceProvidersQueryValidator _validator = new();

    [Fact]
    public void Query_usa_activos_y_la_primera_pagina_de_20_por_defecto()
    {
        var query = new ListInsuranceProvidersQuery();

        query.Search.ShouldBeNull();
        query.IsActive.ShouldBe(true);
        query.PageNumber.ShouldBe(1);
        query.PageSize.ShouldBe(20);
        _validator.Validate(query).IsValid.ShouldBeTrue();
        new ListInsuranceProvidersQuery(IsActive: null).IsActive.ShouldBeNull();
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(2, 20)]
    [InlineData(int.MaxValue, 1)]
    [InlineData(1073741824, 2)]
    [InlineData(21474837, 100)]
    public void Validate_acepta_paginas_con_desplazamiento_representable(int pageNumber, int pageSize)
    {
        _validator.Validate(new ListInsuranceProvidersQuery(PageNumber: pageNumber, PageSize: pageSize))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20, "InsuranceProvider.InvalidPageNumber")]
    [InlineData(-1, 20, "InsuranceProvider.InvalidPageNumber")]
    [InlineData(int.MinValue, 20, "InsuranceProvider.InvalidPageNumber")]
    [InlineData(1, 0, "InsuranceProvider.InvalidPageSize")]
    [InlineData(1, -1, "InsuranceProvider.InvalidPageSize")]
    [InlineData(1, 101, "InsuranceProvider.InvalidPageSize")]
    [InlineData(1, int.MaxValue, "InsuranceProvider.InvalidPageSize")]
    [InlineData(1073741825, 2, "InsuranceProvider.PageOffsetTooLarge")]
    [InlineData(21474838, 100, "InsuranceProvider.PageOffsetTooLarge")]
    [InlineData(int.MaxValue, 100, "InsuranceProvider.PageOffsetTooLarge")]
    public void Validate_rechaza_paginacion_invalida_sin_desbordarse(int pageNumber, int pageSize, string code)
    {
        var result = _validator.Validate(new ListInsuranceProvidersQuery(PageNumber: pageNumber, PageSize: pageSize));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.ErrorCode == code);
    }

    [Fact]
    public void Validate_usa_los_mismos_mensajes_que_el_listado_de_especialidades()
    {
        _validator.Validate(new ListInsuranceProvidersQuery(PageNumber: 0)).Errors
            .ShouldHaveSingleItem().ErrorMessage.ShouldBe("El número de página debe ser mayor o igual a 1.");
        _validator.Validate(new ListInsuranceProvidersQuery(PageSize: 101)).Errors
            .ShouldHaveSingleItem().ErrorMessage.ShouldBe("El tamaño de página debe estar entre 1 y 100.");
        _validator.Validate(new ListInsuranceProvidersQuery(PageNumber: int.MaxValue, PageSize: 100)).Errors
            .ShouldHaveSingleItem().ErrorMessage.ShouldBe("El desplazamiento de la página supera el máximo permitido.");
    }
}
