using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;
using SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;
using SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;
using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorCalendar;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;
using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class DoctorAppointmentsControllerTests
{
    private const int AppointmentId = 99;
    private const int MinimumWords = 30;
    private const string Reason = "Emergencia familiar";
    private const string RejectionReason = "Agenda completa";
    private const int AdditionalHours = 2;
    private const string InvalidAdditionalHoursCode = "Appointment.InvalidAdditionalHours";

    private readonly ICommandHandler<CancelAppointmentByDoctorCommand, Unit> _handler =
        Substitute.For<ICommandHandler<CancelAppointmentByDoctorCommand, Unit>>();

    private readonly ICommandHandler<ConfirmAppointmentCommand, Unit> _confirmHandler =
        Substitute.For<ICommandHandler<ConfirmAppointmentCommand, Unit>>();

    private readonly ICommandHandler<RejectAppointmentCommand, Unit> _rejectHandler =
        Substitute.For<ICommandHandler<RejectAppointmentCommand, Unit>>();

    private readonly ICommandHandler<RescheduleAppointmentByDoctorCommand, Unit> _rescheduleHandler =
        Substitute.For<ICommandHandler<RescheduleAppointmentByDoctorCommand, Unit>>();

    private readonly ICommandHandler<ExtendAppointmentCommand, Unit> _extendHandler =
        Substitute.For<ICommandHandler<ExtendAppointmentCommand, Unit>>();

    private readonly ICommandHandler<CompleteAppointmentCommand, Unit> _completeHandler =
        Substitute.For<ICommandHandler<CompleteAppointmentCommand, Unit>>();

    [Fact]
    public async Task Cancel_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<CancelAppointmentByDoctorCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorAppointmentsController().Cancel(
            AppointmentId, new CancelAppointmentByDoctorRequest(Reason), _handler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _handler.Received(1).HandleAsync(
            new CancelAppointmentByDoctorCommand(AppointmentId, Reason), cancellation.Token);
    }

    public static IEnumerable<object[]> CancelErrors()
    {
        yield return [AppointmentErrors.StatusReasonRequired, 400];
        yield return [AppointmentErrors.ShortNoticeReasonTooShort(MinimumWords), 400];
        yield return [AppointmentErrors.CancellationDoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [AppointmentErrors.InvalidTransition, 409];
    }

    [Theory]
    [MemberData(nameof(CancelErrors))]
    public async Task Cancel_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _handler.HandleAsync(Arg.Any<CancelAppointmentByDoctorCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new DoctorAppointmentsController().Cancel(
            AppointmentId, new CancelAppointmentByDoctorRequest(), _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public async Task Confirm_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        _confirmHandler.HandleAsync(Arg.Any<ConfirmAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorAppointmentsController().Confirm(AppointmentId, _confirmHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _confirmHandler.Received(1).HandleAsync(new ConfirmAppointmentCommand(AppointmentId), cancellation.Token);
    }

    [Fact]
    public async Task Reject_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        _rejectHandler.HandleAsync(Arg.Any<RejectAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorAppointmentsController().Reject(
            AppointmentId, new RejectAppointmentRequest(RejectionReason), _rejectHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _rejectHandler.Received(1).HandleAsync(
            new RejectAppointmentCommand(AppointmentId, RejectionReason), cancellation.Token);
    }

    public static IEnumerable<object[]> DecisionErrors()
    {
        yield return [AppointmentErrors.StatusReasonRequired, 400];
        yield return [AppointmentErrors.DecisionDoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [AppointmentErrors.NotPending, 409];
        yield return [AppointmentErrors.ConfirmationDeadlinePassed, 409];
    }

    [Theory]
    [MemberData(nameof(DecisionErrors))]
    public async Task Confirm_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _confirmHandler.HandleAsync(Arg.Any<ConfirmAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new DoctorAppointmentsController().Confirm(
            AppointmentId, _confirmHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Theory]
    [MemberData(nameof(DecisionErrors))]
    public async Task Reject_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _rejectHandler.HandleAsync(Arg.Any<RejectAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new DoctorAppointmentsController().Reject(
            AppointmentId, new RejectAppointmentRequest(), _rejectHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public async Task Reschedule_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        var startsAt = new DateTimeOffset(2027, 1, 20, 13, 0, 0, TimeSpan.Zero);
        _rescheduleHandler.HandleAsync(Arg.Any<RescheduleAppointmentByDoctorCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorAppointmentsController().Reschedule(
            AppointmentId, new RescheduleAppointmentByDoctorRequest(startsAt), _rescheduleHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _rescheduleHandler.Received(1).HandleAsync(
            new RescheduleAppointmentByDoctorCommand(AppointmentId, startsAt), cancellation.Token);
    }

    public static IEnumerable<object[]> RescheduleErrors()
    {
        yield return [Error.Validation("Appointment.StartsAtRequired", "Debe indicar la nueva hora de la cita."), 400];
        yield return [AppointmentErrors.NewStartNotInFuture, 400];
        yield return [AppointmentErrors.RescheduleDoctorOnly, 403];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [AppointmentErrors.AlreadyStarted, 409];
        yield return [AppointmentErrors.SlotUnavailable, 409];
    }

    [Theory]
    [MemberData(nameof(RescheduleErrors))]
    public async Task Reschedule_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _rescheduleHandler.HandleAsync(Arg.Any<RescheduleAppointmentByDoctorCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new DoctorAppointmentsController().Reschedule(
            AppointmentId, new RescheduleAppointmentByDoctorRequest(), _rescheduleHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void Reschedule_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        AssertDoctorPostEndpoint(nameof(DoctorAppointmentsController.Reschedule), "{id:int}/reschedule");
    }

    [Fact]
    public async Task Extend_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        _extendHandler.HandleAsync(Arg.Any<ExtendAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorAppointmentsController().Extend(
            AppointmentId, new ExtendAppointmentRequest(AdditionalHours), _extendHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _extendHandler.Received(1).HandleAsync(
            new ExtendAppointmentCommand(AppointmentId, AdditionalHours), cancellation.Token);
    }

    [Fact]
    public async Task Complete_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        _completeHandler.HandleAsync(Arg.Any<CompleteAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorAppointmentsController().Complete(AppointmentId, _completeHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _completeHandler.Received(1).HandleAsync(new CompleteAppointmentCommand(AppointmentId), cancellation.Token);
    }

    public static IEnumerable<object[]> ProgressErrors()
    {
        yield return [Error.Validation(InvalidAdditionalHoursCode, "Las horas a sumar no son válidas."), 400];
        yield return [AppointmentErrors.ProgressDoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [AppointmentErrors.NotInProgress, 409];
        yield return [AppointmentErrors.ExtensionUnavailable, 409];
        yield return [AppointmentErrors.SlotUnavailable, 409];
    }

    [Theory]
    [MemberData(nameof(ProgressErrors))]
    public async Task Extend_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _extendHandler.HandleAsync(Arg.Any<ExtendAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new DoctorAppointmentsController().Extend(
            AppointmentId, new ExtendAppointmentRequest(AdditionalHours), _extendHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Theory]
    [MemberData(nameof(ProgressErrors))]
    public async Task Complete_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _completeHandler.HandleAsync(Arg.Any<CompleteAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new DoctorAppointmentsController().Complete(
            AppointmentId, _completeHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void Extend_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        AssertDoctorPostEndpoint(nameof(DoctorAppointmentsController.Extend), "{id:int}/extend");
    }

    [Fact]
    public void Complete_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        AssertDoctorPostEndpoint(nameof(DoctorAppointmentsController.Complete), "{id:int}/complete");
    }

    [Fact]
    public void Cancel_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        var type = typeof(DoctorAppointmentsController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/doctors/me/appointments");

        AssertDoctorPostEndpoint(nameof(DoctorAppointmentsController.Cancel), "{id:int}/cancel");
    }

    [Fact]
    public void Confirm_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        AssertDoctorPostEndpoint(nameof(DoctorAppointmentsController.Confirm), "{id:int}/confirm");
    }

    [Fact]
    public void Reject_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        AssertDoctorPostEndpoint(nameof(DoctorAppointmentsController.Reject), "{id:int}/reject");
    }

    private static void AssertDoctorPostEndpoint(string methodName, string template)
    {
        var method = typeof(DoctorAppointmentsController).GetMethod(methodName)!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBe(template);
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 400).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 404).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 409).Type.ShouldBe(typeof(ProblemDetails));
    }

    private static void AssertProblem(IActionResult result, Error error, int status)
    {
        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }
    private static readonly DateTimeOffset ScheduleStart = new(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);

    private static readonly DoctorScheduleItemDto ScheduleItem = new(
        AppointmentId, 3, "María Pérez", 5, "Cardiología", ScheduleStart, ScheduleStart.AddHours(2),
        new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0), AppointmentStatus.Confirmed, false);

    private static readonly DoctorScheduleItemResponse ScheduleItemResponse = new(
        AppointmentId, 3, "María Pérez", 5, "Cardiología", ScheduleStart, ScheduleStart.AddHours(2),
        new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0), "confirmed", false);

    [Fact]
    public async Task List_pasa_los_filtros_combinados_y_responde_la_pagina()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IQueryHandler<GetDoctorScheduleQuery, PagedResult<DoctorScheduleItemDto>>>();
        handler.HandleAsync(Arg.Any<GetDoctorScheduleQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<DoctorScheduleItemDto>>.Success(new PagedResult<DoctorScheduleItemDto>([ScheduleItem], 2, 10, 11)));
        var request = new DoctorScheduleRequest(
            AppointmentStatus.Confirmed, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), "maria", DoctorScheduleSort.Descending, 2, 10);

        var result = await new DoctorAppointmentsController().List(request, handler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<DoctorScheduleResponse>();
        response.Items.ShouldHaveSingleItem().ShouldBe(ScheduleItemResponse);
        (response.PageNumber, response.PageSize, response.TotalCount).ShouldBe((2, 10, 11));
        await handler.Received(1).HandleAsync(
            new GetDoctorScheduleQuery(AppointmentStatus.Confirmed, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), "maria",
                DoctorScheduleSort.Descending, 2, 10),
            cancellation.Token);
    }

    [Fact]
    public async Task Calendar_pasa_el_rango_y_responde_todas_las_citas()
    {
        var handler = Substitute.For<IQueryHandler<GetDoctorCalendarQuery, IReadOnlyList<DoctorScheduleItemDto>>>();
        handler.HandleAsync(Arg.Any<GetDoctorCalendarQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<DoctorScheduleItemDto>>.Success([ScheduleItem]));

        var result = await new DoctorAppointmentsController().Calendar(
            new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), handler, CancellationToken.None);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<DoctorCalendarResponse>()
            .Items.ShouldHaveSingleItem().ShouldBe(ScheduleItemResponse);
        await handler.Received(1).HandleAsync(
            new GetDoctorCalendarQuery(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)), CancellationToken.None);
    }

    [Fact]
    public async Task Get_responde_el_detalle_y_una_cita_ajena_responde_404()
    {
        var detail = new DoctorAppointmentDetailDto(
            AppointmentId, 3, "María Pérez", "8095550100", "maria@correo.com", 5, "Cardiología", ScheduleStart,
            ScheduleStart.AddHours(2), new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0),
            AppointmentStatus.Pending, false, "Dolor de pecho");
        var handler = Substitute.For<IQueryHandler<GetDoctorAppointmentDetailQuery, DoctorAppointmentDetailDto>>();
        handler.HandleAsync(new GetDoctorAppointmentDetailQuery(AppointmentId), Arg.Any<CancellationToken>())
            .Returns(Result<DoctorAppointmentDetailDto>.Success(detail));
        handler.HandleAsync(new GetDoctorAppointmentDetailQuery(100), Arg.Any<CancellationToken>())
            .Returns(Result<DoctorAppointmentDetailDto>.Failure(AppointmentErrors.NotFound));

        var found = await new DoctorAppointmentsController().Get(AppointmentId, handler, CancellationToken.None);
        var missing = await new DoctorAppointmentsController().Get(100, handler, CancellationToken.None);

        found.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new DoctorAppointmentDetailResponse(
            AppointmentId, 3, "María Pérez", "8095550100", "maria@correo.com", 5, "Cardiología", ScheduleStart,
            ScheduleStart.AddHours(2), new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0),
            "pending", false, "Dolor de pecho"));
        var notFound = missing.ShouldBeOfType<ObjectResult>();
        notFound.StatusCode.ShouldBe(404);
        notFound.Value.ShouldBeOfType<ProblemDetails>().Extensions["code"].ShouldBe("Appointment.NotFound");
    }

    [Theory]
    [InlineData(nameof(DoctorAppointmentsController.List), null)]
    [InlineData(nameof(DoctorAppointmentsController.Calendar), "calendar")]
    [InlineData(nameof(DoctorAppointmentsController.Get), "{id:int}")]
    public void La_agenda_y_el_detalle_son_GET_solo_para_medicos(string action, string? template)
    {
        var method = typeof(DoctorAppointmentsController).GetMethod(action)!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBe(template);
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
    }
}
