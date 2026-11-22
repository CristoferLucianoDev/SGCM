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
        _reader.SearchAsync(null, 1, 20, CancellationToken.None)
            .Returns(new PagedResult<DoctorSearchDto>([], 1, 20, 0));

        var result = await CreateHandler().HandleAsync(new SearchDoctorsQuery(), CancellationToken.None);

        result.Value.PageNumber.ShouldBe(1);
        result.Value.PageSize.ShouldBe(20);
        await _reader.Received(1).SearchAsync(null, 1, 20, CancellationToken.None);
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
        _reader.SearchAsync(expected, 2, 2, ct).Returns(page);

        var result = await CreateHandler().HandleAsync(new SearchDoctorsQuery(input, 2, 2), ct);

        result.Value.ShouldBeSameAs(page);
        result.Value.Items.Select(doctor => doctor.Id).ShouldBe(new[] { 10, 3 });
        await _reader.Received(1).SearchAsync(expected, 2, 2, ct);
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
