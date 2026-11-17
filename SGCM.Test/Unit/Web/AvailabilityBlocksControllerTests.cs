using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AvailabilityBlocksControllerTests
{
    private static readonly CreateAvailabilityBlockRequest Request =
        new(new DateOnly(2027, 1, 5), new TimeOnly(8, 0), new TimeOnly(14, 0));

    private readonly ICommandHandler<CreateAvailabilityBlockCommand, int> _handler =
        Substitute.For<ICommandHandler<CreateAvailabilityBlockCommand, int>>();

    [Fact]
    public async Task Crear_mapea_el_comando_y_responde_201_con_el_id()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<CreateAvailabilityBlockCommand>(), cancellation.Token).Returns(21);

        var result = await new AvailabilityBlocksController().Create(Request, _handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(201);
        response.Value.ShouldBe(new CreateAvailabilityBlockResponse(21));
        await _handler.Received(1).HandleAsync(
            new CreateAvailabilityBlockCommand(Request.Date, Request.StartTime, Request.EndTime), cancellation.Token);
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [DoctorAvailabilityErrors.StartsInPast, 400];
        yield return [DoctorAvailabilityErrors.NotOnHour, 400];
        yield return [DoctorAvailabilityErrors.DurationTooShort, 400];
        yield return [DoctorErrors.NotFound, 404];
        yield return [DoctorAvailabilityErrors.Overlap, 409];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Crear_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _handler.HandleAsync(Arg.Any<CreateAvailabilityBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(error));

        var result = await new AvailabilityBlocksController().Create(Request, _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(status);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Contrato_es_exclusivo_del_rol_Doctor_y_no_recibe_el_medico()
    {
        var type = typeof(AvailabilityBlocksController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/doctors/me/availability-blocks");
        type.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        type.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var method = type.GetMethod(nameof(AvailabilityBlocksController.Create))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBeNull();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        typeof(CreateAvailabilityBlockRequest).GetProperties().Select(property => property.Name)
            .ShouldBe(new[] { "Date", "StartTime", "EndTime" }, ignoreOrder: true);
        typeof(CreateAvailabilityBlockCommand).GetProperties().Select(property => property.Name)
            .ShouldNotContain("DoctorId");
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 201, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 201).Type.ShouldBe(typeof(CreateAvailabilityBlockResponse));
    }
}
