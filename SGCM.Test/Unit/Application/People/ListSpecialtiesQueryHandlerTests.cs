using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class ListSpecialtiesQueryHandlerTests
{
    private readonly ISpecialtyListReader _reader = Substitute.For<ISpecialtyListReader>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    public static IEnumerable<object?[]> VisibilityCases()
    {
        UserRole?[] roles = [null, UserRole.Patient, UserRole.Doctor, UserRole.Administrator];
        bool?[] states = [true, false, null];
        foreach (var role in roles)
        foreach (var state in states)
        {
            yield return [role, state];
        }
    }

    [Theory]
    [MemberData(nameof(VisibilityCases))]
    public async Task Handle_calcula_el_filtro_y_la_visibilidad_del_estado_desde_el_usuario(
        UserRole? role, bool? requestedState)
    {
        _user.IsAuthenticated.Returns(role.HasValue);
        _user.IsInRole(UserRole.Administrator).Returns(role == UserRole.Administrator);
        var administrator = role == UserRole.Administrator;
        bool? expectedState = administrator ? requestedState : true;
        var page = new PagedResult<SpecialtyDto>([], 1, 20, 0);
        _reader.ListAsync(null, expectedState, administrator, 1, 20, CancellationToken.None).Returns(page);

        var query = requestedState == true ? new ListSpecialtiesQuery() : new ListSpecialtiesQuery(IsActive: requestedState);
        var result = await CreateHandler().HandleAsync(query, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(page);
        await _reader.Received(1).ListAsync(null, expectedState, administrator, 1, 20, CancellationToken.None);
    }

    [Fact]
    public async Task Handle_no_concede_privilegios_a_un_rol_sin_autenticacion()
    {
        _user.IsAuthenticated.Returns(false);
        _user.IsInRole(UserRole.Administrator).Returns(true);
        _reader.ListAsync(null, true, false, 1, 20, CancellationToken.None)
            .Returns(new PagedResult<SpecialtyDto>([], 1, 20, 0));

        await CreateHandler().HandleAsync(new ListSpecialtiesQuery(IsActive: null), CancellationToken.None);

        await _reader.Received(1).ListAsync(null, true, false, 1, 20, CancellationToken.None);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n ", null)]
    [InlineData("  GINÉCO  ", "GINÉCO")]
    [InlineData("%_", "%_")]
    public async Task Handle_prepara_la_busqueda_sin_normalizar_mayusculas_ni_tildes(string? search, string? expected)
    {
        using var cancellation = new CancellationTokenSource();
        var page = new PagedResult<SpecialtyDto>([new(7, "Ginecología", "Descripción.", null)], 2, 10, 11);
        _reader.ListAsync(expected, true, false, 2, 10, cancellation.Token).Returns(page);

        var result = await CreateHandler().HandleAsync(
            new ListSpecialtiesQuery(Search: search, PageNumber: 2, PageSize: 10), cancellation.Token);

        result.Value.ShouldBeSameAs(page);
        await _reader.Received(1).ListAsync(expected, true, false, 2, 10, cancellation.Token);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public async Task Handle_devuelve_paginas_vacias_como_exito_y_conserva_el_total(int totalCount)
    {
        _reader.ListAsync(null, true, false, 2, 20, CancellationToken.None)
            .Returns(new PagedResult<SpecialtyDto>([], 2, 20, totalCount));

        var result = await CreateHandler().HandleAsync(new ListSpecialtiesQuery(PageNumber: 2), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBeEmpty();
        result.Value.TotalCount.ShouldBe(totalCount);
    }

    [Theory]
    [InlineData(0, 20, "Specialty.InvalidPageNumber")]
    [InlineData(1, 101, "Specialty.InvalidPageSize")]
    [InlineData(int.MaxValue, 100, "Specialty.PageOffsetTooLarge")]
    public async Task Handle_rechaza_la_paginacion_antes_de_consultar(int pageNumber, int pageSize, string code)
    {
        var result = await CreateHandler().HandleAsync(
            new ListSpecialtiesQuery(PageNumber: pageNumber, PageSize: pageSize), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(code);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_no_consulta_si_el_token_ya_esta_cancelado()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateHandler().HandleAsync(new ListSpecialtiesQuery(), cancellation.Token));

        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Handle_propaga_la_cancelacion_del_lector()
    {
        using var cancellation = new CancellationTokenSource();
        _reader.ListAsync(null, true, false, 1, 20, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<PagedResult<SpecialtyDto>>(cancellation.Token);
        });

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateHandler().HandleAsync(new ListSpecialtiesQuery(), cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
    }

    private ListSpecialtiesQueryHandler CreateHandler() => new(_reader, _user, new ListSpecialtiesQueryValidator());
}
