using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Data.Domain.Common;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class SearchDoctorsQueryHandlerTests
{
    private readonly IDoctorSearchReader _reader = Substitute.For<IDoctorSearchReader>();

    [Fact]
    public async Task La_consulta_sin_parametros_usa_la_primera_pagina_por_defecto()
    {
        DoctorSearchCriteria criteria = new(null, 1, 20, null, null, null);
        _reader.SearchAsync(criteria, CancellationToken.None)
            .Returns(new PagedResult<DoctorSearchDto>([], 1, 20, 0));

        var result = await CreateHandler().HandleAsync(new SearchDoctorsQuery(), CancellationToken.None);

        result.Value.PageNumber.ShouldBe(1);
        result.Value.PageSize.ShouldBe(20);
        await _reader.Received(1).SearchAsync(criteria, CancellationToken.None);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n ", null)]
    [InlineData("  JOSÉ Ma  ", "JOSÉ Ma")]
    [InlineData(" Ana  María ", "Ana  María")]
    [InlineData(" %_\\' ", "%_\\'")]
    public async Task Prepara_el_texto_y_conserva_la_pagina_y_el_orden_del_lector(string? input, string? expected)
    {
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        var first = new DoctorSearchDto(10, "Ana", "Pérez", 1, "Provincia", 2, "Municipio",
            "Dirección", 100m, 5, "Biografía", [], []);
        var page = new PagedResult<DoctorSearchDto>([first, first with { Id = 3 }], 2, 2, 5);
        DoctorSearchCriteria criteria = new(expected, 2, 2, null, null, null);
        _reader.SearchAsync(criteria, ct).Returns(page);

        var result = await CreateHandler().HandleAsync(new SearchDoctorsQuery(input, 2, 2), ct);

        result.Value.ShouldBeSameAs(page);
        result.Value.Items.Select(doctor => doctor.Id).ShouldBe(new[] { 10, 3 });
        await _reader.Received(1).SearchAsync(criteria, ct);
    }

    [Theory]
    [InlineData(null, 11, null, null, null)]
    [InlineData(null, null, 22, null, null)]
    [InlineData(null, null, null, 33, null)]
    [InlineData(null, 11, 22, 33, null)]
    [InlineData(" Ana  María ", 11, 22, 33, "Ana  María")]
    public async Task Propaga_los_filtros_opcionales_con_la_paginacion_y_el_token(
        string? search, int? specialty, int? province, int? municipality, string? normalized)
    {
        using var cancellation = new CancellationTokenSource();
        var criteria = new DoctorSearchCriteria(normalized, 2, 13, specialty, province, municipality);
        var expected = new PagedResult<DoctorSearchDto>([], 2, 13, 7);
        _reader.SearchAsync(criteria, cancellation.Token).Returns(expected);

        var result = await CreateHandler().HandleAsync(
            new SearchDoctorsQuery(search, 2, 13, specialty, province, municipality), cancellation.Token);

        result.Value.ShouldBeSameAs(expected);
        await _reader.Received(1).SearchAsync(criteria, cancellation.Token);
    }

    [Theory]
    [InlineData(0, null, null, "Doctor.InvalidSpecialtyId")]
    [InlineData(-1, null, null, "Doctor.InvalidSpecialtyId")]
    [InlineData(null, 0, null, "Doctor.InvalidProvinceId")]
    [InlineData(null, -1, null, "Doctor.InvalidProvinceId")]
    [InlineData(null, null, 0, "Doctor.InvalidMunicipalityId")]
    [InlineData(null, null, -1, "Doctor.InvalidMunicipalityId")]
    public async Task Rechaza_filtros_no_positivos_antes_de_leer(
        int? specialty, int? province, int? municipality, string code)
    {
        var result = await CreateHandler().HandleAsync(
            new SearchDoctorsQuery(SpecialtyId: specialty, ProvinceId: province, MunicipalityId: municipality),
            CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(code);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 20, "Doctor.InvalidPageNumber")]
    [InlineData(1, 0, "Doctor.InvalidPageSize")]
    [InlineData(1, 101, "Doctor.InvalidPageSize")]
    [InlineData(int.MaxValue, 100, "Doctor.PageOffsetTooLarge")]
    public async Task Rechaza_paginacion_invalida_antes_de_leer(int number, int size, string code)
    {
        var result = await CreateHandler().HandleAsync(new SearchDoctorsQuery(null, number, size), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(code);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Rechaza_la_solicitud_sin_leer_y_prioriza_el_primer_error_de_paginacion()
    {
        (SearchDoctorsQuery Query, string ErrorCode)[] invalidQueries =
        [
            (new(PageNumber: 0, PageSize: 0, SpecialtyId: 0), "Doctor.InvalidPageNumber"),
            (new(PageSize: 101, ProvinceId: -1), "Doctor.InvalidPageSize"),
            (new(PageNumber: int.MaxValue, PageSize: 100, MunicipalityId: 0), "Doctor.PageOffsetTooLarge")
        ];
        foreach (var (query, code) in invalidQueries)
        {
            var result = await CreateHandler().HandleAsync(query, CancellationToken.None);
            result.Error.Type.ShouldBe(ErrorType.Validation);
            result.Error.Code.ShouldBe(code);
        }

        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Observa_la_cancelacion_antes_de_leer()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateHandler().HandleAsync(new SearchDoctorsQuery(), cancellation.Token));

        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private SearchDoctorsQueryHandler CreateHandler() => new(_reader, new SearchDoctorsQueryValidator());
}
