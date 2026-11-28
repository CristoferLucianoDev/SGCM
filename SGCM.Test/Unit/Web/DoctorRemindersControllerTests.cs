using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.Commands.ScheduleReminder;
using SGCM.Data.Application.Messaging.Queries.GetReminderPreview;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Api.Controllers.Messaging;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Messaging;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class DoctorRemindersControllerTests
{
    private const int AppointmentId = 99;
    private const int TemplateId = 1;

    private static readonly DateTimeOffset SendAt = new(2027, 1, 11, 13, 0, 0, TimeSpan.Zero);

    private readonly ICommandHandler<ScheduleReminderCommand, ScheduledReminderDto> _scheduleHandler =
        Substitute.For<ICommandHandler<ScheduleReminderCommand, ScheduledReminderDto>>();

    private readonly IQueryHandler<GetReminderPreviewQuery, ReminderPreviewDto> _previewHandler =
        Substitute.For<IQueryHandler<GetReminderPreviewQuery, ReminderPreviewDto>>();

    [Fact]
    public async Task Preview_pasa_la_cita_la_plantilla_y_el_token_y_responde_200_con_el_texto()
    {
        using var cancellation = new CancellationTokenSource();
        _previewHandler.HandleAsync(Arg.Any<GetReminderPreviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReminderPreviewDto>.Success(new ReminderPreviewDto(TemplateId, "Asunto", "Hola Ana")));

        var result = await new DoctorRemindersController().Preview(
            AppointmentId, TemplateId, _previewHandler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new ReminderPreviewResponse(TemplateId, "Asunto", "Hola Ana"));
        await _previewHandler.Received(1).HandleAsync(
            new GetReminderPreviewQuery(AppointmentId, TemplateId), cancellation.Token);
    }

    public static IEnumerable<object[]> PreviewErrors()
    {
        yield return [Error.Validation("ReminderTemplate.InvalidId", "El identificador de la plantilla debe ser mayor que cero."), 400];
        yield return [ReminderErrors.DoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [ReminderTemplateErrors.NotFound, 404];
        yield return [ReminderErrors.AppointmentNotEligible, 409];
    }

    [Theory]
    [MemberData(nameof(PreviewErrors))]
    public async Task Preview_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _previewHandler.HandleAsync(Arg.Any<GetReminderPreviewQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReminderPreviewDto>.Failure(error));

        var result = await new DoctorRemindersController().Preview(
            AppointmentId, TemplateId, _previewHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void Preview_es_un_GET_solo_para_medicos_con_sus_respuestas()
    {
        typeof(DoctorRemindersController).GetCustomAttribute<RouteAttribute>()!.Template
            .ShouldBe("api/doctors/me/appointments");
        var method = typeof(DoctorRemindersController).GetMethod(nameof(DoctorRemindersController.Preview))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBe("{id:int}/reminders/preview");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        method.GetParameters().Single(parameter => parameter.Name == "templateId")
            .GetCustomAttribute<FromQueryAttribute>().ShouldNotBeNull();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 200, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(typeof(ReminderPreviewResponse));
        responses.Single(value => value.StatusCode == 404).Type.ShouldBe(typeof(ProblemDetails));
    }

    [Fact]
    public async Task Schedule_pasa_el_comando_y_el_token_y_responde_201_con_el_recordatorio()
    {
        using var cancellation = new CancellationTokenSource();
        _scheduleHandler.HandleAsync(Arg.Any<ScheduleReminderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScheduledReminderDto>.Success(new ScheduledReminderDto(700, AppointmentId, TemplateId, "Hola Ana", SendAt)));

        var result = await new DoctorRemindersController().Schedule(
            AppointmentId, new ScheduleReminderRequest("Hola Ana", SendAt, TemplateId), _scheduleHandler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        response.Value.ShouldBe(new ScheduledReminderResponse(700, AppointmentId, TemplateId, "Hola Ana", SendAt));
        await _scheduleHandler.Received(1).HandleAsync(
            new ScheduleReminderCommand(AppointmentId, TemplateId, "Hola Ana", SendAt), cancellation.Token);
    }

    public static IEnumerable<object[]> ScheduleErrors()
    {
        yield return [ReminderErrors.MessageRequired, 400];
        yield return [ReminderErrors.MessageTooLong, 400];
        yield return [ReminderErrors.ScheduledInPast, 400];
        yield return [ReminderErrors.DoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [ReminderTemplateErrors.NotFound, 404];
        yield return [ReminderErrors.AppointmentNotEligible, 409];
    }

    [Theory]
    [MemberData(nameof(ScheduleErrors))]
    public async Task Schedule_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _scheduleHandler.HandleAsync(Arg.Any<ScheduleReminderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ScheduledReminderDto>.Failure(error));

        var result = await new DoctorRemindersController().Schedule(
            AppointmentId, new ScheduleReminderRequest(), _scheduleHandler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void Schedule_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        var method = typeof(DoctorRemindersController).GetMethod(nameof(DoctorRemindersController.Schedule))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBe("{id:int}/reminders");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 201, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 201).Type.ShouldBe(typeof(ScheduledReminderResponse));
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
