using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;
using SGCM.Data.Application.People.Queries.ListInsuranceProviders;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class InsuranceProvidersControllerTests
{
    private static readonly RegisterInsuranceProviderRequest Request = new(
        "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null);

    private readonly ICommandHandler<RegisterInsuranceProviderCommand, int> _handler =
        Substitute.For<ICommandHandler<RegisterInsuranceProviderCommand, int>>();

    private readonly ICommandHandler<UpdateInsuranceProviderCommand, Unit> _updateHandler =
        Substitute.For<ICommandHandler<UpdateInsuranceProviderCommand, Unit>>();

    private readonly IQueryHandler<ListInsuranceProvidersQuery, PagedResult<InsuranceProviderDto>> _queryHandler =
        Substitute.For<IQueryHandler<ListInsuranceProvidersQuery, PagedResult<InsuranceProviderDto>>>();

    [Fact]
    public async Task Register_mapea_la_solicitud_y_devuelve_201_con_el_id()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<RegisterInsuranceProviderCommand>(), cancellation.Token).Returns(42);

        var result = await new InsuranceProvidersController().Register(Request, _handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(201);
        response.Value.ShouldBeOfType<RegisterInsuranceProviderResponse>().Id.ShouldBe(42);
        await _handler.Received(1).HandleAsync(
            new RegisterInsuranceProviderCommand(
                "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null),
            cancellation.Token);
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 409)]
    public async Task Register_devuelve_errores_mediante_ApiControllerBase(bool duplicate, int expectedStatus)
    {
        var error = duplicate ? InsuranceProviderErrors.NameAlreadyInUse : InsuranceProviderErrors.NameRequired;
        _handler.HandleAsync(Arg.Any<RegisterInsuranceProviderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(error));

        var result = await new InsuranceProvidersController().Register(Request, _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(expectedStatus);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(expectedStatus);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public async Task Update_mapea_el_id_y_los_campos_propaga_el_token_y_devuelve_204()
    {
        using var cancellation = new CancellationTokenSource();
        var request = new UpdateInsuranceProviderRequest { Name = "  Seguros del Caribe  ", Website = null };
        _updateHandler.HandleAsync(Arg.Any<UpdateInsuranceProviderCommand>(), cancellation.Token).Returns(Unit.Value);

        var result = await new InsuranceProvidersController().Update(42, request, _updateHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>().StatusCode.ShouldBe(204);
        await _updateHandler.Received(1).HandleAsync(new UpdateInsuranceProviderCommand(42, request.Changes), cancellation.Token);
        var received = _updateHandler.ReceivedCalls().Single().GetArguments()[0].ShouldBeOfType<UpdateInsuranceProviderCommand>();
        received.Changes.Name.ShouldBe(Change<string?>.Set("  Seguros del Caribe  "));
        received.Changes.Website.ShouldBe(Change<string?>.Set(null));
        received.Changes.Address.IsProvided.ShouldBeFalse();
        _handler.ReceivedCalls().ShouldBeEmpty();
        _queryHandler.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("InsuranceProvider.InvalidId", 400, 0, "{\"name\":\"Seguros\"}")]
    [InlineData("InsuranceProvider.NoFieldsProvided", 400, 42, "{}")]
    [InlineData("InsuranceProvider.NameRequired", 400, 42, "{\"name\":null}")]
    [InlineData("InsuranceProvider.WebsiteInvalid", 400, 42, "{\"website\":\"ftp://sitio.com\"}")]
    [InlineData("InsuranceProvider.NotFound", 404, 42, "{\"name\":\"Seguros\"}")]
    [InlineData("InsuranceProvider.NameAlreadyInUse", 409, 42, "{\"name\":\"Seguros\"}")]
    public async Task Update_utiliza_ErrorResult_con_el_estado_y_codigo_correspondientes(
        string code, int status, int id, string json)
    {
        var error = code switch
        {
            "InsuranceProvider.InvalidId" => InsuranceProviderErrors.InvalidId,
            "InsuranceProvider.NoFieldsProvided" => InsuranceProviderErrors.NoFieldsProvided,
            "InsuranceProvider.NameRequired" => InsuranceProviderErrors.NameRequired,
            "InsuranceProvider.WebsiteInvalid" => InsuranceProviderErrors.WebsiteInvalid,
            "InsuranceProvider.NotFound" => InsuranceProviderErrors.NotFound,
            _ => InsuranceProviderErrors.NameAlreadyInUse
        };
        var request = JsonSerializer.Deserialize<UpdateInsuranceProviderRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var command = new UpdateInsuranceProviderCommand(id, request.Changes);
        // El validador real confirma que los errores 400 son los que produce la validación de la aplicación.
        var validation = new UpdateInsuranceProviderCommandValidator().Validate(command);
        if (status == 400)
        {
            validation.IsValid.ShouldBeFalse();
            validation.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
        }
        else
        {
            validation.IsValid.ShouldBeTrue();
        }
        _updateHandler.HandleAsync(command, CancellationToken.None).Returns(Result<Unit>.Failure(error));

        var result = await new InsuranceProvidersController().Update(id, request, _updateHandler, CancellationToken.None);

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
        _updateHandler.HandleAsync(Arg.Any<UpdateInsuranceProviderCommand>(), cancellation.Token)
            .Returns(Task.FromCanceled<Result<Unit>>(cancellation.Token));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => new InsuranceProvidersController().Update(
            42, new UpdateInsuranceProviderRequest { Name = "Seguros" }, _updateHandler, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _updateHandler.Received(1).HandleAsync(
            Arg.Is<UpdateInsuranceProviderCommand>(command => command.Id == 42 && command.Changes.Name.IsProvided),
            cancellation.Token);
    }

    [Fact]
    public void Update_es_PATCH_administrativo_con_id_de_ruta_y_cuerpo_JSON()
    {
        var controller = typeof(InsuranceProvidersController);
        controller.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();

        var update = controller.GetMethod(nameof(InsuranceProvidersController.Update))!;
        update.GetCustomAttributes<HttpPatchAttribute>().ShouldHaveSingleItem().Template.ShouldBe("{id:int}");
        update.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        update.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Administrator));
        update.GetParameters().Single(parameter => parameter.Name == "id")
            .GetCustomAttribute<FromRouteAttribute>().ShouldNotBeNull();
        update.GetParameters().Single(parameter => parameter.ParameterType == typeof(UpdateInsuranceProviderRequest))
            .GetCustomAttribute<FromBodyAttribute>().ShouldNotBeNull();
        update.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(ICommandHandler<UpdateInsuranceProviderCommand, Unit>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        var responses = update.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(attribute => attribute.StatusCode)
            .ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Where(attribute => attribute.StatusCode is 400 or 404 or 409)
            .ShouldAllBe(attribute => attribute.Type == typeof(ProblemDetails));
    }

    [Fact]
    public void Register_es_POST_exclusivo_para_administradores()
    {
        var controller = typeof(InsuranceProvidersController);
        controller.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/insurance-providers");
        controller.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();

        var register = controller.GetMethod(nameof(InsuranceProvidersController.Register))!;
        register.GetCustomAttributes<HttpPostAttribute>().ShouldHaveSingleItem().Template.ShouldBeNull();
        register.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        register.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Administrator));
        register.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(ICommandHandler<RegisterInsuranceProviderCommand, int>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
    }

    [Theory]
    [InlineData(InsuranceProviderStatusFilter.Active, true)]
    [InlineData(InsuranceProviderStatusFilter.Inactive, false)]
    [InlineData(InsuranceProviderStatusFilter.All, null)]
    public async Task List_mapea_la_solicitud_propaga_el_token_y_devuelve_200_con_el_contrato_HTTP(
        InsuranceProviderStatusFilter status, bool? expectedState)
    {
        using var cancellation = new CancellationTokenSource();
        var itemState = expectedState ?? false;
        var page = new PagedResult<InsuranceProviderDto>(
            [new(42, "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null, itemState)],
            2, 10, 11);
        _queryHandler.HandleAsync(Arg.Any<ListInsuranceProvidersQuery>(), cancellation.Token).Returns(page);

        var result = await new InsuranceProvidersController().List(
            new ListInsuranceProvidersRequest("  UNIVÉR%_  ", 2, 10, status), _queryHandler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>();
        response.StatusCode.ShouldBe(200);
        var body = response.Value.ShouldBeOfType<ListInsuranceProvidersResponse>();
        body.Items.ShouldHaveSingleItem().ShouldBe(new InsuranceProviderResponse(
            42, "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null, itemState));
        body.PageNumber.ShouldBe(2);
        body.PageSize.ShouldBe(10);
        body.TotalCount.ShouldBe(11);
        body.Message.ShouldBeNull();
        await _queryHandler.Received(1).HandleAsync(
            new ListInsuranceProvidersQuery("  UNIVÉR%_  ", expectedState, 2, 10), cancellation.Token);
    }

    [Theory]
    [InlineData(0, "No existen registros de proveedores de seguros")]
    [InlineData(4, null)]
    public async Task List_devuelve_200_vacio_y_el_mensaje_depende_del_total(int totalCount, string? message)
    {
        _queryHandler.HandleAsync(new ListInsuranceProvidersQuery(PageNumber: 3), CancellationToken.None)
            .Returns(new PagedResult<InsuranceProviderDto>([], 3, 20, totalCount));

        var result = await new InsuranceProvidersController().List(
            new ListInsuranceProvidersRequest(PageNumber: 3), _queryHandler, CancellationToken.None);

        var response = result.ShouldBeOfType<OkObjectResult>();
        response.StatusCode.ShouldBe(200);
        var body = response.Value.ShouldBeOfType<ListInsuranceProvidersResponse>();
        body.Items.ShouldBeEmpty();
        body.PageNumber.ShouldBe(3);
        body.PageSize.ShouldBe(20);
        body.TotalCount.ShouldBe(totalCount);
        body.Message.ShouldBe(message);
    }

    [Theory]
    [InlineData("InsuranceProvider.InvalidPageNumber")]
    [InlineData("InsuranceProvider.InvalidPageSize")]
    [InlineData("InsuranceProvider.PageOffsetTooLarge")]
    public async Task List_devuelve_400_y_el_codigo_del_error_del_handler(string code)
    {
        var error = Error.Validation(code, "Paginación inválida.");
        _queryHandler.HandleAsync(Arg.Any<ListInsuranceProvidersQuery>(), CancellationToken.None)
            .Returns(Result<PagedResult<InsuranceProviderDto>>.Failure(error));

        var result = await new InsuranceProvidersController().List(
            new ListInsuranceProvidersRequest(), _queryHandler, CancellationToken.None);

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
        _queryHandler.HandleAsync(Arg.Any<ListInsuranceProvidersQuery>(), cancellation.Token)
            .Returns(Task.FromCanceled<Result<PagedResult<InsuranceProviderDto>>>(cancellation.Token));

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => new InsuranceProvidersController().List(
            new ListInsuranceProvidersRequest(), _queryHandler, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _queryHandler.Received(1).HandleAsync(new ListInsuranceProvidersQuery(), cancellation.Token);
    }

    [Fact]
    public void List_es_GET_publico_sin_Authorize_ni_rol_y_solo_declara_200_y_400()
    {
        var list = typeof(InsuranceProvidersController).GetMethod(nameof(InsuranceProvidersController.List))!;

        list.GetCustomAttributes<HttpGetAttribute>().ShouldHaveSingleItem().Template.ShouldBeNull();
        list.GetCustomAttributes<AllowAnonymousAttribute>().ShouldHaveSingleItem();
        list.GetCustomAttributes<AuthorizeAttribute>().ShouldBeEmpty();
        list.GetParameters().Single(parameter => parameter.ParameterType == typeof(ListInsuranceProvidersRequest))
            .GetCustomAttribute<FromQueryAttribute>().ShouldNotBeNull();
        list.GetParameters()
            .Single(parameter => parameter.ParameterType == typeof(IQueryHandler<ListInsuranceProvidersQuery, PagedResult<InsuranceProviderDto>>))
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        var responses = list.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 200, 400 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(typeof(ListInsuranceProvidersResponse));
        responses.Single(value => value.StatusCode == 400).Type.ShouldBe(typeof(ProblemDetails));
    }
}
