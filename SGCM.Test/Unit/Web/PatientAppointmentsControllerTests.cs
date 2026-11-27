using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class PatientAppointmentsControllerTests
{
    private const int AppointmentId = 99;
    private const int PageNumber = 2;
    private const int PageSize = 50;
    private const int TotalCount = 120;
    private const int ConfirmationHours = 48;
    private const int HoursLeftToConfirm = 24;

    private static readonly TimeSpan HoursLeft = TimeSpan.FromHours(HoursLeftToConfirm);

    private static readonly DateOnly From = new(2027, 1, 1);
    private static readonly DateOnly To = new(2027, 3, 1);
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<GetPatientAppointmentHistoryQuery, PagedResult<PatientAppointmentDto>> _listHandler =
        Substitute.For<IQueryHandler<GetPatientAppointmentHistoryQuery, PagedResult<PatientAppointmentDto>>>();

    private readonly IQueryHandler<GetPatientAppointmentDetailQuery, PatientAppointmentDetailDto> _getHandler =
        Substitute.For<IQueryHandler<GetPatientAppointmentDetailQuery, PatientAppointmentDetailDto>>();

    private readonly ICommandHandler<RescheduleAppointmentCommand, Unit> _rescheduleHandler =
        Substitute.For<ICommandHandler<RescheduleAppointmentCommand, Unit>>();

    [Fact]
    public async Task Reschedule_pasa_el_comando_y_el_token_al_handler_y_responde_204()
    {
        using var cancellation = new CancellationTokenSource();
        _rescheduleHandler.HandleAsync(Arg.Any<RescheduleAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new PatientAppointmentsController().Reschedule(
            AppointmentId, new RescheduleAppointmentRequest(StartsAt), _rescheduleHandler, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>();
        await _rescheduleHandler.Received(1).HandleAsync(
            new RescheduleAppointmentCommand(AppointmentId, StartsAt), cancellation.Token);
    }

    public static IEnumerable<object[]> RescheduleErrors()
    {
        yield return [Error.Validation("Appointment.StartsAtRequired", "Debe indicar la nueva hora de la cita."), 400];
        yield return [AppointmentErrors.TooSoon, 400];
        yield return [AppointmentErrors.ReschedulePatientOnly, 403];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [AppointmentErrors.RescheduleDeadlinePassed, 409];
        yield return [AppointmentErrors.SlotUnavailable, 409];
        yield return [AppointmentErrors.PatientOverlap, 409];
    }

    [Theory]
    [MemberData(nameof(RescheduleErrors))]
    public async Task Reschedule_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _rescheduleHandler.HandleAsync(Arg.Any<RescheduleAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));

        var result = await new PatientAppointmentsController().Reschedule(
            AppointmentId, new RescheduleAppointmentRequest(StartsAt), _rescheduleHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void Reschedule_es_un_POST_con_id_entero_solo_para_pacientes_con_sus_respuestas()
    {
        var method = typeof(PatientAppointmentsController).GetMethod(nameof(PatientAppointmentsController.Reschedule))!;

        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBe("{id:int}/reschedule");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Patient));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 400).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 404).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 409).Type.ShouldBe(typeof(ProblemDetails));
    }

    [Fact]
    public async Task List_pasa_la_consulta_y_el_token_al_handler_y_responde_200_con_la_respuesta_mapeada()
    {
        using var cancellation = new CancellationTokenSource();
        var page = new PagedResult<PatientAppointmentDto>(
            [CreateAppointment(AppointmentStatus.Cancelled)], PageNumber, PageSize, TotalCount);
        _listHandler.HandleAsync(Arg.Any<GetPatientAppointmentHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<PatientAppointmentDto>>.Success(page));
        var request = new ListPatientAppointmentsRequest(AppointmentStatusFilter.Cancelled, From, To, PageNumber, PageSize);

        var result = await new PatientAppointmentsController().List(request, _listHandler, cancellation.Token);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var response = ok.Value.ShouldBeOfType<ListPatientAppointmentsResponse>();
        response.Items.ShouldBe(new[]
        {
            new PatientAppointmentResponse(
                AppointmentId,
                StartsAt,
                StartsAt.AddHours(2),
                new DateOnly(2027, 1, 12),
                new TimeOnly(9, 0),
                new TimeOnly(11, 0),
                3,
                "Luis Pérez",
                5,
                "Cardiología",
                "cancelled")
        });
        response.PageNumber.ShouldBe(PageNumber);
        response.PageSize.ShouldBe(PageSize);
        response.TotalCount.ShouldBe(TotalCount);
        response.Message.ShouldBeNull();
        await _listHandler.Received(1).HandleAsync(
            new GetPatientAppointmentHistoryQuery(AppointmentStatus.Cancelled, From, To, PageNumber, PageSize),
            cancellation.Token);
    }

    [Fact]
    public async Task Get_pasa_el_id_y_el_token_al_handler_y_responde_200_con_el_detalle()
    {
        using var cancellation = new CancellationTokenSource();
        var allowed = new AllowedAppointmentActions(
            [AppointmentAction.Cancel, AppointmentAction.ChangeDoctor], false, StartsAt.AddHours(-ConfirmationHours), HoursLeft);
        var detail = new PatientAppointmentDetailDto(
            CreateAppointment(AppointmentStatus.Rejected), "Dolor", "Sin cupo", allowed, null);
        _getHandler.HandleAsync(Arg.Any<GetPatientAppointmentDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PatientAppointmentDetailDto>.Success(detail));

        var result = await new PatientAppointmentsController().Get(AppointmentId, _getHandler, cancellation.Token);

        var ok = result.ShouldBeOfType<OkObjectResult>();
        var response = ok.Value.ShouldBeOfType<PatientAppointmentDetailResponse>();
        response.Appointment.ShouldBe(CreateAppointment(AppointmentStatus.Rejected).ToResponse());
        response.ReasonForVisit.ShouldBe("Dolor");
        response.StatusReason.ShouldBe("Sin cupo");
        response.AllowedActions.ShouldBe(new[] { "cancel", "changeDoctor" });
        response.ConfirmationDeadline.ShouldBe(StartsAt.AddHours(-ConfirmationHours));
        response.MinutesUntilConfirmationDeadline.ShouldBe((int)HoursLeft.TotalMinutes);
        await _getHandler.Received(1).HandleAsync(new GetPatientAppointmentDetailQuery(AppointmentId), cancellation.Token);
    }

    public static IEnumerable<object[]> ListErrors()
    {
        yield return [Error.Validation("Appointment.InvalidPageNumber", "La página debe ser al menos 1."), 400];
        yield return [AppointmentErrors.HistoryPatientOnly, 403];
        yield return [PatientErrors.ProfileNotFound, 404];
    }

    public static IEnumerable<object[]> GetErrors()
    {
        yield return [Error.Validation("Appointment.InvalidAppointmentId", "El identificador debe ser mayor que cero."), 400];
        yield return [AppointmentErrors.HistoryPatientOnly, 403];
        yield return [AppointmentErrors.NotFound, 404];
    }

    [Theory]
    [MemberData(nameof(ListErrors))]
    public async Task List_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _listHandler.HandleAsync(Arg.Any<GetPatientAppointmentHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PagedResult<PatientAppointmentDto>>.Failure(error));

        var result = await new PatientAppointmentsController().List(
            new ListPatientAppointmentsRequest(), _listHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Theory]
    [MemberData(nameof(GetErrors))]
    public async Task Get_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _getHandler.HandleAsync(Arg.Any<GetPatientAppointmentDetailQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<PatientAppointmentDetailDto>.Failure(error));

        var result = await new PatientAppointmentsController().Get(AppointmentId, _getHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void List_es_un_GET_solo_para_pacientes_con_sus_respuestas_y_el_request_en_la_query()
    {
        var type = typeof(PatientAppointmentsController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/patients/me/appointments");

        var method = type.GetMethod(nameof(PatientAppointmentsController.List))!;

        AssertGetEndpoint(method, template: string.Empty, typeof(ListPatientAppointmentsResponse));
        method.GetParameters().Single(parameter => parameter.ParameterType == typeof(ListPatientAppointmentsRequest))
            .GetCustomAttribute<FromQueryAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void Get_es_un_GET_con_id_entero_solo_para_pacientes_con_sus_respuestas()
    {
        var method = typeof(PatientAppointmentsController).GetMethod(nameof(PatientAppointmentsController.Get))!;

        AssertGetEndpoint(method, template: "{id:int}", typeof(PatientAppointmentDetailResponse));
    }

    private static void AssertGetEndpoint(MethodInfo method, string template, Type okType)
    {
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        (verb.Template ?? string.Empty).ShouldBe(template);
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Patient));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(okType);
        responses.Single(value => value.StatusCode == 400).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 404).Type.ShouldBe(typeof(ProblemDetails));
    }

    private static void AssertProblem(IActionResult result, Error error, int status)
    {
        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    private static PatientAppointmentDto CreateAppointment(AppointmentStatus status) => new(
        AppointmentId,
        StartsAt,
        StartsAt.AddHours(2),
        new DateOnly(2027, 1, 12),
        new TimeOnly(9, 0),
        new TimeOnly(11, 0),
        3,
        "Luis Pérez",
        5,
        "Cardiología",
        status);
}
