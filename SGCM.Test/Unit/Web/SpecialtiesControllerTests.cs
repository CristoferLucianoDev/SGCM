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
using SGCM.Data.Application.People.Commands.ActivateSpecialty;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Application.People.Commands.DeactivateSpecialty;
using SGCM.Data.Application.People.Commands.UpdateSpecialty;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

public class SpecialtiesControllerTests
{
    private readonly ICommandHandler<CreateSpecialtyCommand, int> _handler = Substitute.For<ICommandHandler<CreateSpecialtyCommand, int>>();
    private readonly ICommandHandler<UpdateSpecialtyCommand, Unit> _updateHandler =
        Substitute.For<ICommandHandler<UpdateSpecialtyCommand, Unit>>();
    private readonly IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>> _queryHandler =
        Substitute.For<IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>>();

    [Fact]
    public async Task Deactivate_pasa_el_id_y_token_al_handler_y_devuelve_204()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<DeactivateSpecialtyCommand, Unit>>();
        handler.HandleAsync(new DeactivateSpecialtyCommand(42), cancellation.Token).Returns(Unit.Value);

        var result = await new SpecialtiesController().Deactivate(42, handler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>().StatusCode.ShouldBe(204);
        await handler.Received(1).HandleAsync(new DeactivateSpecialtyCommand(42), cancellation.Token);
        handler.ReceivedCalls().ShouldHaveSingleItem();
        _handler.ReceivedCalls().ShouldBeEmpty();
        _updateHandler.ReceivedCalls().ShouldBeEmpty();
        _queryHandler.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(400, 0)]
    [InlineData(404, 42)]
    [InlineData(409, 42)]
    public async Task Deactivate_utiliza_ErrorResult_con_el_estado_codigo_y_mensaje(int status, int id)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<DeactivateSpecialtyCommand, Unit>>();
        var error = status switch
        {
            400 => SpecialtyErrors.InvalidId,
            404 => SpecialtyErrors.NotFound,
            _ => ActivationErrors.AlreadyInactive(nameof(Specialty))
        };
        handler.HandleAsync(new DeactivateSpecialtyCommand(id), cancellation.Token).Returns(Result<Unit>.Failure(error));

        var result = await new SpecialtiesController().Deactivate(id, handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(status);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
        await handler.Received(1).HandleAsync(new DeactivateSpecialtyCommand(id), cancellation.Token);
        handler.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Deactivate_propaga_la_cancelacion_del_handler()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = Substitute.For<ICommandHandler<DeactivateSpecialtyCommand, Unit>>();
        handler.HandleAsync(new DeactivateSpecialtyCommand(42), cancellation.Token)
            .Returns(Task.FromCanceled<Result<Unit>>(cancellation.Token));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            new SpecialtiesController().Deactivate(42, handler, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await handler.Received(1).HandleAsync(new DeactivateSpecialtyCommand(42), cancellation.Token);
    }

    [Fact]
    public void Deactivate_es_PATCH_administrativo_con_id_de_ruta_sin_cuerpo_y_handler_de_servicios()
    {
        var controller = typeof(SpecialtiesController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/specialties");
        controller.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        var method = controller.GetMethod(nameof(SpecialtiesController.Deactivate))!;
        method.GetCustomAttributes<HttpPatchAttribute>().ShouldHaveSingleItem()
            .Template.ShouldBe("{id:int}/deactivate");
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem()
            .Roles.ShouldBe(nameof(UserRole.Administrator));
        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(3);
        parameters.Single(parameter => parameter.Name == "id").ParameterType.ShouldBe(typeof(int));
        parameters.Single(parameter => parameter.Name == "id")
            .GetCustomAttribute<FromRouteAttribute>().ShouldNotBeNull();
        parameters.Single(parameter => parameter.ParameterType == typeof(ICommandHandler<DeactivateSpecialtyCommand, Unit>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        parameters.Single(parameter => parameter.ParameterType == typeof(CancellationToken)).ShouldNotBeNull();
        parameters.ShouldAllBe(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() == null);
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(attribute => attribute.StatusCode)
            .ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Where(attribute => attribute.StatusCode == 400 || attribute.StatusCode == 404 || attribute.StatusCode == 409)
            .ShouldAllBe(attribute => attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public async Task Activate_pasa_el_id_y_token_al_handler_y_devuelve_204()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<ActivateSpecialtyCommand, Unit>>();
        handler.HandleAsync(new ActivateSpecialtyCommand(42), cancellation.Token).Returns(Unit.Value);

        var result = await new SpecialtiesController().Activate(42, handler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>().StatusCode.ShouldBe(204);
        await handler.Received(1).HandleAsync(new ActivateSpecialtyCommand(42), cancellation.Token);
        handler.ReceivedCalls().ShouldHaveSingleItem();
        _handler.ReceivedCalls().ShouldBeEmpty();
        _updateHandler.ReceivedCalls().ShouldBeEmpty();
        _queryHandler.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(400, 0)]
    [InlineData(404, 42)]
    [InlineData(409, 42)]
    public async Task Activate_utiliza_ErrorResult_con_el_estado_codigo_y_mensaje(int status, int id)
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<ActivateSpecialtyCommand, Unit>>();
        var error = status switch
        {
            400 => SpecialtyErrors.InvalidId,
            404 => SpecialtyErrors.NotFound,
            _ => ActivationErrors.AlreadyActive(nameof(Specialty))
        };
        handler.HandleAsync(new ActivateSpecialtyCommand(id), cancellation.Token).Returns(Result<Unit>.Failure(error));

        var result = await new SpecialtiesController().Activate(id, handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(status);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
        await handler.Received(1).HandleAsync(new ActivateSpecialtyCommand(id), cancellation.Token);
        handler.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Activate_propaga_la_cancelacion_del_handler()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = Substitute.For<ICommandHandler<ActivateSpecialtyCommand, Unit>>();
        handler.HandleAsync(new ActivateSpecialtyCommand(42), cancellation.Token)
            .Returns(Task.FromCanceled<Result<Unit>>(cancellation.Token));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            new SpecialtiesController().Activate(42, handler, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await handler.Received(1).HandleAsync(new ActivateSpecialtyCommand(42), cancellation.Token);
    }

    [Fact]
    public void Activate_es_PATCH_administrativo_con_id_de_ruta_sin_cuerpo_y_handler_de_servicios()
    {
        var controller = typeof(SpecialtiesController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/specialties");
        controller.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        var method = controller.GetMethod(nameof(SpecialtiesController.Activate))!;
        method.GetCustomAttributes<HttpPatchAttribute>().ShouldHaveSingleItem()
            .Template.ShouldBe("{id:int}/activate");
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem()
            .Roles.ShouldBe(nameof(UserRole.Administrator));
        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(3);
        parameters.Single(parameter => parameter.Name == "id").ParameterType.ShouldBe(typeof(int));
        parameters.Single(parameter => parameter.Name == "id")
            .GetCustomAttribute<FromRouteAttribute>().ShouldNotBeNull();
        parameters.Single(parameter => parameter.ParameterType == typeof(ICommandHandler<ActivateSpecialtyCommand, Unit>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        parameters.Single(parameter => parameter.ParameterType == typeof(CancellationToken)).ShouldNotBeNull();
        parameters.ShouldAllBe(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() == null);
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(attribute => attribute.StatusCode)
            .ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Where(attribute => attribute.StatusCode == 400 || attribute.StatusCode == 404 || attribute.StatusCode == 409)
            .ShouldAllBe(attribute => attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public async Task Update_mapea_el_id_y_los_campos_propaga_el_token_y_devuelve_204()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new UpdateSpecialtyRequest { Name = "  NEUROLOGÍA  ", Description = "Descripción nueva." };
        _updateHandler.HandleAsync(Arg.Any<UpdateSpecialtyCommand>(), cancellation.Token).Returns(Unit.Value);

        var result = await new SpecialtiesController().Update(42, request, _updateHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>().StatusCode.ShouldBe(204);
        await _updateHandler.Received(1).HandleAsync(
            new UpdateSpecialtyCommand(42, true, "  NEUROLOGÍA  ", true, "Descripción nueva."), cancellation.Token);
        _handler.ReceivedCalls().ShouldBeEmpty();
        _queryHandler.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Specialty.NameRequired", 400, 42, "{\"name\":null}")]
    [InlineData("Specialty.DescriptionRequired", 400, 42, "{\"description\":null}")]
    [InlineData("Specialty.NoFieldsProvided", 400, 42, "{}")]
    [InlineData("Specialty.InvalidId", 400, 0, "{\"name\":\"Neurología\"}")]
    [InlineData("Specialty.NotFound", 404, 42, "{\"name\":\"Neurología\"}")]
    [InlineData("Specialty.NameAlreadyInUse", 409, 42, "{\"name\":\"Neurología\"}")]
    public async Task Update_utiliza_ErrorResult_con_el_estado_y_codigo_correspondientes(string code, int status, int id, string json)
    {
        var error = code switch
        {
            "Specialty.NameRequired" => SpecialtyErrors.NameRequired,
            "Specialty.DescriptionRequired" => SpecialtyErrors.DescriptionRequired,
            "Specialty.NoFieldsProvided" => SpecialtyErrors.NoFieldsProvided,
            "Specialty.InvalidId" => SpecialtyErrors.InvalidId,
            "Specialty.NotFound" => SpecialtyErrors.NotFound,
            _ => SpecialtyErrors.NameAlreadyInUse
        };
        var request = JsonSerializer.Deserialize<UpdateSpecialtyRequest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var command = new UpdateSpecialtyCommand(id, request.NameProvided, request.Name, request.DescriptionProvided, request.Description);
        var validation = new UpdateSpecialtyCommandValidator().Validate(command);
        if (status == 400)
        {
            validation.IsValid.ShouldBeFalse();
            validation.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
        }
        else
        {
            validation.IsValid.ShouldBeTrue();
        }
        _updateHandler.HandleAsync(command, CancellationToken.None)
            .Returns(Result<Unit>.Failure(error));

        var result = await new SpecialtiesController().Update(id, request, _updateHandler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(status);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(code);
        await _updateHandler.Received(1).HandleAsync(command, CancellationToken.None);
    }

    [Fact]
    public async Task Update_propaga_la_cancelacion_del_handler()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _updateHandler.HandleAsync(Arg.Any<UpdateSpecialtyCommand>(), cancellation.Token)
            .Returns(Task.FromCanceled<Result<Unit>>(cancellation.Token));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => new SpecialtiesController().Update(
            42, new UpdateSpecialtyRequest { Name = "Neurología" }, _updateHandler, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _updateHandler.Received(1).HandleAsync(
            new UpdateSpecialtyCommand(42, true, "Neurología", false, null), cancellation.Token);
    }

    [Fact]
    public void Update_es_PATCH_administrativo_con_id_de_ruta_y_cuerpo_JSON()
    {
        var controller = typeof(SpecialtiesController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/specialties");
        controller.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        var update = controller.GetMethod(nameof(SpecialtiesController.Update))!;
        update.GetCustomAttributes<HttpPatchAttribute>().ShouldHaveSingleItem().Template.ShouldBe("{id:int}");
        update.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        update.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Administrator));
        update.GetParameters().Single(parameter => parameter.Name == "id")
            .GetCustomAttribute<FromRouteAttribute>().ShouldNotBeNull();
        update.GetParameters().Single(parameter => parameter.ParameterType == typeof(UpdateSpecialtyRequest))
            .GetCustomAttribute<FromBodyAttribute>().ShouldNotBeNull();
        update.GetParameters().Single(parameter => parameter.ParameterType == typeof(ICommandHandler<UpdateSpecialtyCommand, Unit>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        update.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(attribute => attribute.StatusCode)
            .ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
    }

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
