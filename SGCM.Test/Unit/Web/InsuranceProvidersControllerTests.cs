using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class InsuranceProvidersControllerTests
{
    private static readonly RegisterInsuranceProviderRequest Request = new(
        "Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null);

    private readonly ICommandHandler<RegisterInsuranceProviderCommand, int> _handler =
        Substitute.For<ICommandHandler<RegisterInsuranceProviderCommand, int>>();

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
}
