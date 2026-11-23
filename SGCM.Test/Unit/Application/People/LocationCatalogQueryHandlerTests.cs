using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Queries.ListMunicipalities;
using SGCM.Data.Application.People.Queries.ListProvinces;
using SGCM.Data.Domain.Common;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class LocationCatalogQueryHandlerTests
{
    private readonly ILocationCatalogReader _reader = Substitute.For<ILocationCatalogReader>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Provincias_devuelve_el_catalogo_incluso_si_esta_vacio_y_propaga_el_token(bool hasProvinces)
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<ProvinceDto> expected = hasProvinces ? [new(1, "Dajabón")] : [];
        _reader.ListProvincesAsync(cancellation.Token).Returns(expected);

        var result = await new ListProvincesQueryHandler(_reader).HandleAsync(new(), cancellation.Token);

        result.Value.ShouldBeSameAs(expected);
        await _reader.Received(1).ListProvincesAsync(cancellation.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Municipios_rechaza_el_id_invalido_antes_de_leer(int id)
    {
        var result = await MunicipalitiesHandler().HandleAsync(new(id), CancellationToken.None);

        result.Error.Code.ShouldBe("Location.InvalidProvinceId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Municipios_devuelve_not_found_si_la_provincia_no_existe()
    {
        _reader.ListMunicipalitiesAsync(30, CancellationToken.None)
            .Returns(Task.FromResult<IReadOnlyList<MunicipalityDto>?>(null));

        var result = await MunicipalitiesHandler().HandleAsync(new(30), CancellationToken.None);

        result.Error.Code.ShouldBe("Location.ProvinceNotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        await _reader.Received(1).ListMunicipalitiesAsync(30, CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Municipios_devuelve_el_catalogo_de_la_provincia_y_propaga_el_token(bool hasMunicipalities)
    {
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<MunicipalityDto> expected = hasMunicipalities ? [new(13, "Polo"), new(24, "Vicente Noble")] : [];
        _reader.ListMunicipalitiesAsync(7, cancellation.Token).Returns(expected);

        var result = await MunicipalitiesHandler().HandleAsync(new(7), cancellation.Token);

        result.Value.ShouldBeSameAs(expected);
        await _reader.Received(1).ListMunicipalitiesAsync(7, cancellation.Token);
    }

    [Fact]
    public async Task Las_consultas_propagaran_los_fallos_del_reader()
    {
        var failure = new InvalidOperationException("Fallo sintético de lectura.");
        _reader.ListProvincesAsync(CancellationToken.None)
            .Returns(Task.FromException<IReadOnlyList<ProvinceDto>>(failure));
        _reader.ListMunicipalitiesAsync(1, CancellationToken.None)
            .Returns(Task.FromException<IReadOnlyList<MunicipalityDto>?>(failure));

        (await Should.ThrowAsync<InvalidOperationException>(() =>
            new ListProvincesQueryHandler(_reader).HandleAsync(new(), CancellationToken.None))).ShouldBeSameAs(failure);
        (await Should.ThrowAsync<InvalidOperationException>(() =>
            MunicipalitiesHandler().HandleAsync(new(1), CancellationToken.None))).ShouldBeSameAs(failure);
    }

    private ListMunicipalitiesQueryHandler MunicipalitiesHandler() =>
        new(_reader, new ListMunicipalitiesQueryValidator());
}
