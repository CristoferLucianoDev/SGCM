using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AvailabilitySeriesControllerTests
{
    private static readonly CreateAvailabilitySeriesRequest Request = new(
        [1, 3, 5], new TimeOnly(8, 0), new TimeOnly(14, 0), new DateOnly(2027, 1, 4), new DateOnly(2027, 6, 30));

    private static readonly CreateAvailabilitySeriesCommand Command = new(
        Request.DaysOfWeek, Request.StartTime, Request.EndTime, Request.StartDate, Request.EndDate);

    private readonly ICommandHandler<CreateAvailabilitySeriesCommand, int> _handler =
        Substitute.For<ICommandHandler<CreateAvailabilitySeriesCommand, int>>();

    [Fact]
    public async Task Crear_mapea_el_comando_y_responde_201_con_el_id()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<CreateAvailabilitySeriesCommand>(), cancellation.Token).Returns(15);

        var result = await new AvailabilitySeriesController().Create(Request, _handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(201);
        response.Value.ShouldBe(new CreateAvailabilitySeriesResponse(15));
        await _handler.Received(1).HandleAsync(
            Arg.Is<CreateAvailabilitySeriesCommand>(command =>
                command.DaysOfWeek!.SequenceEqual(Command.DaysOfWeek!)
                && command.StartTime == Command.StartTime && command.EndTime == Command.EndTime
                && command.StartDate == Command.StartDate && command.EndDate == Command.EndDate),
            cancellation.Token);
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [DoctorAvailabilitySeriesErrors.StartDateInPast, 400];
        yield return [DoctorAvailabilitySeriesErrors.DurationTooShort, 400];
        yield return [DoctorErrors.NotFound, 404];
        yield return [DoctorAvailabilitySeriesErrors.Overlap, 409];
        yield return [DoctorAvailabilityErrors.Overlap, 409];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Crear_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _handler.HandleAsync(Arg.Any<CreateAvailabilitySeriesCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<int>.Failure(error));

        var result = await new AvailabilitySeriesController().Create(Request, _handler, CancellationToken.None);

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
        var type = typeof(AvailabilitySeriesController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/doctors/me/availability-series");
        type.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        type.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var method = type.GetMethod(nameof(AvailabilitySeriesController.Create))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBeNull();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        typeof(CreateAvailabilitySeriesRequest).GetProperties().Select(property => property.Name)
            .ShouldBe(new[] { "DaysOfWeek", "StartTime", "EndTime", "StartDate", "EndDate" }, ignoreOrder: true);
        typeof(CreateAvailabilitySeriesCommand).GetProperties().Select(property => property.Name)
            .ShouldNotContain("DoctorId");
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 201, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 201).Type.ShouldBe(typeof(CreateAvailabilitySeriesResponse));
    }

    [Fact]
    public async Task Update_pasa_la_serie_el_patron_y_el_motivo_y_responde_200()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<UpdateAvailabilitySeriesCommand, UpdatedAvailabilitySeriesDto>>();
        handler.HandleAsync(Arg.Any<UpdateAvailabilitySeriesCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdatedAvailabilitySeriesDto>.Success(new UpdatedAvailabilitySeriesDto(new DateOnly(2027, 1, 11), 2)));
        var request = new UpdateAvailabilitySeriesRequest([1, 2], new TimeOnly(10, 0), new TimeOnly(16, 0), null, "Motivo");

        var result = await new AvailabilitySeriesController().Update(15, request, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value
            .ShouldBe(new UpdateAvailabilitySeriesResponse(new DateOnly(2027, 1, 11), 2));
        await handler.Received(1).HandleAsync(
            Arg.Is<UpdateAvailabilitySeriesCommand>(command => command.SeriesId == 15
                && command.DaysOfWeek!.SequenceEqual(new[] { 1, 2 })
                && command.StartTime == request.StartTime && command.EndTime == request.EndTime
                && command.EndDate == null && command.Reason == "Motivo"),
            cancellation.Token);
    }

    public static IEnumerable<object[]> UpdateErrors()
    {
        yield return [DoctorAvailabilitySeriesErrors.InvalidDateRange, 400];
        yield return [AppointmentErrors.StatusReasonRequired, 400];
        yield return [DoctorAvailabilitySeriesErrors.NotFound, 404];
        yield return [DoctorAvailabilitySeriesErrors.Overlap, 409];
    }

    [Theory]
    [MemberData(nameof(UpdateErrors))]
    public async Task Update_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        var handler = Substitute.For<ICommandHandler<UpdateAvailabilitySeriesCommand, UpdatedAvailabilitySeriesDto>>();
        handler.HandleAsync(Arg.Any<UpdateAvailabilitySeriesCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<UpdatedAvailabilitySeriesDto>.Failure(error));

        var result = await new AvailabilitySeriesController().Update(
            15, new UpdateAvailabilitySeriesRequest([1], new TimeOnly(10, 0), new TimeOnly(16, 0), null), handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Update_es_un_PUT_solo_para_medicos()
    {
        var method = typeof(AvailabilitySeriesController).GetMethod(nameof(AvailabilitySeriesController.Update))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "PUT" });
        verb.Template.ShouldBe("{seriesId:int}");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404, 409 }, ignoreOrder: true);
    }
}
