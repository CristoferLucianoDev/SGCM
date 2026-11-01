using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public class SpecialtiesControllerTests
{
    private readonly ICommandHandler<CreateSpecialtyCommand, int> _handler = Substitute.For<ICommandHandler<CreateSpecialtyCommand, int>>();
    private readonly IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>> _queryHandler =
        Substitute.For<IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>>();

    [Fact]
    public async Task Create_mapea_la_solicitud_y_devuelve_201_con_el_id()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<CreateSpecialtyCommand>(), cancellation.Token).Returns(42);
        var controller = new SpecialtiesController();

        var result = await controller.Create(new CreateSpecialtyRequest("CARDIOLOGÍA", "Descripción válida."), _handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(201);
        response.Value.ShouldBeOfType<CreateSpecialtyResponse>().Id.ShouldBe(42);
        await _handler.Received(1).HandleAsync(new CreateSpecialtyCommand("CARDIOLOGÍA", "Descripción válida."), cancellation.Token);
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 409)]
    public async Task Create_devuelve_errores_mediante_ApiControllerBase(bool duplicate, int expectedStatus)
    {
        var error = duplicate ? SpecialtyErrors.NameAlreadyInUse : SpecialtyErrors.NameRequired;
        _handler.HandleAsync(Arg.Any<CreateSpecialtyCommand>(), Arg.Any<CancellationToken>()).Returns(Result<int>.Failure(error));

        var result = await new SpecialtiesController().Create(
            new CreateSpecialtyRequest("Cardiología", "Descripción válida."), _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(expectedStatus);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(expectedStatus);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Theory]
    [InlineData(SpecialtyStatusFilter.Active, true)]
    [InlineData(SpecialtyStatusFilter.Inactive, false)]
    [InlineData(SpecialtyStatusFilter.All, null)]
    public async Task List_mapea_la_solicitud_propaga_el_token_y_devuelve_200_con_el_contrato_HTTP(
        SpecialtyStatusFilter status, bool? expectedState)
    {
        using var cancellation = new CancellationTokenSource();
        var itemState = expectedState ?? false;
        var page = new PagedResult<SpecialtyDto>([new(42, "Ginecología", "Descripción.", itemState)], 2, 10, 11);
        _queryHandler.HandleAsync(Arg.Any<ListSpecialtiesQuery>(), cancellation.Token).Returns(page);

        var result = await new SpecialtiesController().List(
            new ListSpecialtiesRequest("  GINÉCO%_  ", 2, 10, status), _queryHandler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>();
        response.StatusCode.ShouldBe(200);
        var body = response.Value.ShouldBeOfType<ListSpecialtiesResponse>();
        body.Items.ShouldHaveSingleItem().ShouldBe(new SpecialtyResponse(42, "Ginecología", "Descripción.", itemState));
        body.PageNumber.ShouldBe(2);
        body.PageSize.ShouldBe(10);
        body.TotalCount.ShouldBe(11);
        body.Message.ShouldBeNull();
        await _queryHandler.Received(1).HandleAsync(
            new ListSpecialtiesQuery("  GINÉCO%_  ", expectedState, 2, 10), cancellation.Token);
    }

    [Theory]
    [InlineData(0, "No existen especialidades disponibles")]
    [InlineData(4, null)]
    public async Task List_devuelve_200_vacio_y_el_mensaje_depende_del_total(int totalCount, string? message)
    {
        _queryHandler.HandleAsync(new ListSpecialtiesQuery(PageNumber: 3), CancellationToken.None)
            .Returns(new PagedResult<SpecialtyDto>([], 3, 20, totalCount));

        var result = await new SpecialtiesController().List(
            new ListSpecialtiesRequest(PageNumber: 3), _queryHandler, CancellationToken.None);

        var response = result.ShouldBeOfType<OkObjectResult>();
        response.StatusCode.ShouldBe(200);
        var body = response.Value.ShouldBeOfType<ListSpecialtiesResponse>();
        body.Items.ShouldBeEmpty();
        body.PageNumber.ShouldBe(3);
        body.PageSize.ShouldBe(20);
        body.TotalCount.ShouldBe(totalCount);
        body.Message.ShouldBe(message);
    }

    [Theory]
    [InlineData("Specialty.InvalidPageNumber")]
    [InlineData("Specialty.InvalidPageSize")]
    [InlineData("Specialty.PageOffsetTooLarge")]
    public async Task List_devuelve_400_y_el_codigo_del_error_del_handler(string code)
    {
        var error = Error.Validation(code, "Paginación inválida.");
        _queryHandler.HandleAsync(Arg.Any<ListSpecialtiesQuery>(), CancellationToken.None)
            .Returns(Result<PagedResult<SpecialtyDto>>.Failure(error));

        var result = await new SpecialtiesController().List(
            new ListSpecialtiesRequest(), _queryHandler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(400);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(400);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(code);
    }

    [Fact]
    public async Task List_propaga_la_cancelacion_del_handler()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _queryHandler.HandleAsync(Arg.Any<ListSpecialtiesQuery>(), cancellation.Token)
            .Returns(Task.FromCanceled<Result<PagedResult<SpecialtyDto>>>(cancellation.Token));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => new SpecialtiesController().List(
            new ListSpecialtiesRequest(), _queryHandler, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _queryHandler.Received(1).HandleAsync(new ListSpecialtiesQuery(), cancellation.Token);
    }

    [Fact]
    public void List_es_GET_publico_y_Create_conserva_POST_exclusivo_para_administradores()
    {
        var controller = typeof(SpecialtiesController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/specialties");
        controller.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        var list = controller.GetMethod(nameof(SpecialtiesController.List))!;
        list.GetCustomAttributes<HttpGetAttribute>().ShouldHaveSingleItem().Template.ShouldBeNull();
        list.GetCustomAttributes<AllowAnonymousAttribute>().ShouldHaveSingleItem();
        list.GetCustomAttributes<AuthorizeAttribute>().ShouldBeEmpty();
        list.GetParameters().Single(parameter => parameter.ParameterType == typeof(ListSpecialtiesRequest))
            .GetCustomAttribute<FromQueryAttribute>().ShouldNotBeNull();
        list.GetParameters().Single(parameter => parameter.ParameterType == typeof(IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();

        var create = controller.GetMethod(nameof(SpecialtiesController.Create))!;
        create.GetCustomAttributes<HttpPostAttribute>().ShouldHaveSingleItem().Template.ShouldBeNull();
        create.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        create.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Administrator));
    }

    [Theory]
    [InlineData("999")]
    [InlineData("Unknown")]
    public async Task List_MVC_rechaza_el_estado_invalido_con_400_antes_de_ejecutar_la_consulta(string status)
    {
        using var server = CreateMvcServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync($"/api/specialties?status={status}");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("status").GetInt32().ShouldBe(400);
        problem.GetProperty("errors").EnumerateObject()
            .ShouldContain(error => string.Equals(error.Name, "Status", StringComparison.OrdinalIgnoreCase));
        _queryHandler.ReceivedCalls().ShouldBeEmpty();
        _handler.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task List_MVC_sin_token_enlaza_los_valores_por_defecto_y_serializa_la_pagina()
    {
        _queryHandler.HandleAsync(Arg.Any<ListSpecialtiesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new PagedResult<SpecialtyDto>([new(42, "Ginecología", "Descripción.", null)], 1, 20, 7));
        using var server = CreateMvcServer();
        using var client = server.CreateClient();

        using var response = await client.GetAsync("/api/specialties");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("pageNumber").GetInt32().ShouldBe(1);
        body.GetProperty("pageSize").GetInt32().ShouldBe(20);
        body.GetProperty("totalCount").GetInt32().ShouldBe(7);
        var item = body.GetProperty("items").EnumerateArray().ShouldHaveSingleItem();
        item.GetProperty("id").GetInt32().ShouldBe(42);
        item.GetProperty("name").GetString().ShouldBe("Ginecología");
        item.GetProperty("description").GetString().ShouldBe("Descripción.");
        item.TryGetProperty("isActive", out _).ShouldBeFalse();
        body.TryGetProperty("message", out _).ShouldBeFalse();
        await _queryHandler.Received(1).HandleAsync(new ListSpecialtiesQuery(), Arg.Any<CancellationToken>());
        _handler.ReceivedCalls().ShouldBeEmpty();
    }

    private TestServer CreateMvcServer() => new(new WebHostBuilder()
        .ConfigureServices(services =>
        {
            services.AddControllers().AddApplicationPart(typeof(SpecialtiesController).Assembly);
            services.AddSingleton(_queryHandler);
        })
        .Configure(app =>
        {
            app.UseRouting();
            app.UseEndpoints(endpoints => endpoints.MapControllers());
        }));
}
