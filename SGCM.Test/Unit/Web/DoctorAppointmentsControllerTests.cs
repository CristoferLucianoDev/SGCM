using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;
using SGCM.Data.Application.Scheduling.Commands.ConfirmAppointment;
using SGCM.Data.Application.Scheduling.Commands.RejectAppointment;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class DoctorAppointmentsControllerTests
{
    private const int AppointmentId = 99;
    private const int MinimumWords = 30;
    private const string Reason = "Emergencia familiar";
    private const string RejectionReason = "Agenda completa";

    private readonly ICommandHandler<CancelAppointmentByDoctorCommand, Unit> _handler =
        Substitute.For<ICommandHandler<CancelAppointmentByDoctorCommand, Unit>>();

    private readonly ICommandHandler<ConfirmAppointmentCommand, Unit> _confirmHandler =
        Substitute.For<ICommandHandler<ConfirmAppointmentCommand, Unit>>();

    private readonly ICommandHandler<RejectAppointmentCommand, Unit> _rejectHandler =
        Substitute.For<ICommandHandler<RejectAppointmentCommand, Unit>>();

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
}
