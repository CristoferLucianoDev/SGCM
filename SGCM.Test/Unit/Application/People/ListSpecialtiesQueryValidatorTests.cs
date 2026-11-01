using SGCM.Data.Application.People.Queries.ListSpecialties;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class ListSpecialtiesQueryValidatorTests
{
    private readonly ListSpecialtiesQueryValidator _validator = new();

    [Fact]
    public void Query_usa_activas_y_la_primera_pagina_de_20_por_defecto()
    {
        var query = new ListSpecialtiesQuery();

        query.Search.ShouldBeNull();
        query.IsActive.ShouldBe(true);
        query.PageNumber.ShouldBe(1);
        query.PageSize.ShouldBe(20);
        _validator.Validate(query).IsValid.ShouldBeTrue();
        new ListSpecialtiesQuery(IsActive: null).IsActive.ShouldBeNull();
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
        _validator.Validate(new ListSpecialtiesQuery(PageNumber: pageNumber, PageSize: pageSize))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, 20, "Specialty.InvalidPageNumber")]
    [InlineData(-1, 20, "Specialty.InvalidPageNumber")]
    [InlineData(int.MinValue, 20, "Specialty.InvalidPageNumber")]
    [InlineData(1, 0, "Specialty.InvalidPageSize")]
    [InlineData(1, -1, "Specialty.InvalidPageSize")]
    [InlineData(1, 101, "Specialty.InvalidPageSize")]
    [InlineData(1, int.MaxValue, "Specialty.InvalidPageSize")]
    [InlineData(1073741825, 2, "Specialty.PageOffsetTooLarge")]
    [InlineData(21474838, 100, "Specialty.PageOffsetTooLarge")]
    [InlineData(int.MaxValue, 100, "Specialty.PageOffsetTooLarge")]
    public void Validate_rechaza_paginacion_invalida_sin_desbordarse(int pageNumber, int pageSize, string code)
    {
        var result = _validator.Validate(new ListSpecialtiesQuery(PageNumber: pageNumber, PageSize: pageSize));

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.ErrorCode == code);
    }
}
