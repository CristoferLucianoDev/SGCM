using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
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

    [Fact]
    public async Task GetAffectedAppointments_pasa_el_bloque_y_el_horario_nuevo_y_responde_200_con_el_aviso()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IQueryHandler<GetAffectedAppointmentsQuery, AffectedAppointmentsDto>>();
        var startsAt = new DateTimeOffset(2027, 1, 12, 14, 0, 0, TimeSpan.Zero);
        handler.HandleAsync(Arg.Any<GetAffectedAppointmentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<AffectedAppointmentsDto>.Success(new AffectedAppointmentsDto(2, true, true, 6, 30)));

        var result = await new AvailabilityBlocksController().GetAffectedAppointments(
            40, startsAt, startsAt.AddHours(2), handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new AffectedAppointmentsResponse(2, true, true, 6, 30));
        await handler.Received(1).HandleAsync(
            new GetAffectedAppointmentsQuery(40, startsAt, startsAt.AddHours(2)), cancellation.Token);
    }

    [Fact]
    public async Task GetAffectedAppointments_de_un_bloque_ajeno_responde_404()
    {
        var handler = Substitute.For<IQueryHandler<GetAffectedAppointmentsQuery, AffectedAppointmentsDto>>();
        handler.HandleAsync(Arg.Any<GetAffectedAppointmentsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<AffectedAppointmentsDto>.Failure(DoctorAvailabilityErrors.NotFound));

        var result = await new AvailabilityBlocksController().GetAffectedAppointments(40, null, null, handler, CancellationToken.None);

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(404);
    }

    [Fact]
    public void GetAffectedAppointments_es_un_GET_solo_para_medicos()
    {
        var method = typeof(AvailabilityBlocksController).GetMethod(nameof(AvailabilityBlocksController.GetAffectedAppointments))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBe("{blockId:int}/affected-appointments");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
    }

    [Fact]
    public async Task Close_pasa_el_bloque_y_el_motivo_y_responde_200_con_las_citas_canceladas()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<CloseAvailabilityBlockCommand, ClosedAvailabilityBlockDto>>();
        handler.HandleAsync(Arg.Any<CloseAvailabilityBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ClosedAvailabilityBlockDto>.Success(new ClosedAvailabilityBlockDto(2)));

        var result = await new AvailabilityBlocksController().Close(
            40, new CloseAvailabilityBlockRequest("Congreso"), handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new CloseAvailabilityBlockResponse(2));
        await handler.Received(1).HandleAsync(new CloseAvailabilityBlockCommand(40, "Congreso"), cancellation.Token);
    }

    public static IEnumerable<object[]> CloseErrors()
    {
        yield return [AppointmentErrors.StatusReasonRequired, 400];
        yield return [AppointmentErrors.ShortNoticeReasonTooShort(30), 400];
        yield return [DoctorAvailabilityErrors.AlreadyClosed, 400];
        yield return [DoctorAvailabilityErrors.NotFound, 404];
        yield return [DoctorErrors.NotFound, 404];
    }

    [Theory]
    [MemberData(nameof(CloseErrors))]
    public async Task Close_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        var handler = Substitute.For<ICommandHandler<CloseAvailabilityBlockCommand, ClosedAvailabilityBlockDto>>();
        handler.HandleAsync(Arg.Any<CloseAvailabilityBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ClosedAvailabilityBlockDto>.Failure(error));

        var result = await new AvailabilityBlocksController().Close(40, new CloseAvailabilityBlockRequest(), handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Close_es_un_POST_solo_para_medicos()
    {
        var method = typeof(AvailabilityBlocksController).GetMethod(nameof(AvailabilityBlocksController.Close))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBe("{blockId:int}/close");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
    }

    [Fact]
    public async Task Reschedule_pasa_el_bloque_el_horario_y_el_motivo_y_responde_200()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<RescheduleAvailabilityBlockCommand, RescheduledAvailabilityBlockDto>>();
        handler.HandleAsync(Arg.Any<RescheduleAvailabilityBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<RescheduledAvailabilityBlockDto>.Success(new RescheduledAvailabilityBlockDto(1)));
        var request = new RescheduleAvailabilityBlockRequest(new DateOnly(2027, 1, 20), new TimeOnly(10, 0), new TimeOnly(16, 0), "Motivo");

        var result = await new AvailabilityBlocksController().Reschedule(40, request, handler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new RescheduleAvailabilityBlockResponse(1));
        await handler.Received(1).HandleAsync(
            new RescheduleAvailabilityBlockCommand(40, request.Date, request.StartTime, request.EndTime, "Motivo"), cancellation.Token);
    }

    public static IEnumerable<object[]> RescheduleErrors()
    {
        yield return [SchedulingErrors.CurrentWeekLocked, 400];
        yield return [SchedulingErrors.NextWeekLocked, 400];
        yield return [AppointmentErrors.StatusReasonRequired, 400];
        yield return [DoctorAvailabilityErrors.NotFound, 404];
        yield return [DoctorAvailabilityErrors.Overlap, 409];
    }

    [Theory]
    [MemberData(nameof(RescheduleErrors))]
    public async Task Reschedule_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        var handler = Substitute.For<ICommandHandler<RescheduleAvailabilityBlockCommand, RescheduledAvailabilityBlockDto>>();
        handler.HandleAsync(Arg.Any<RescheduleAvailabilityBlockCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<RescheduledAvailabilityBlockDto>.Failure(error));

        var result = await new AvailabilityBlocksController().Reschedule(
            40, new RescheduleAvailabilityBlockRequest(new DateOnly(2027, 1, 20), new TimeOnly(10, 0), new TimeOnly(16, 0)),
            handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Reschedule_es_un_PUT_solo_para_medicos()
    {
        var method = typeof(AvailabilityBlocksController).GetMethod(nameof(AvailabilityBlocksController.Reschedule))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "PUT" });
        verb.Template.ShouldBe("{blockId:int}");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404, 409 }, ignoreOrder: true);
    }
}
