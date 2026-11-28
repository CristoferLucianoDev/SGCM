using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Web.Api.Controllers.Messaging;
using SGCM.Web.Contracts.Messaging;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class DoctorReminderTemplatesControllerTests
{
    private const int Id = 5;
    private static readonly ReminderTemplateDto Dto = new(Id, "Indicaciones", "Asunto", "Hola {PatientName}.", true, false);
    private static readonly ReminderTemplateResponse Response = new(Id, "Indicaciones", "Asunto", "Hola {PatientName}.", true, false);
    private static readonly ReminderTemplateRequest Request = new("Indicaciones", "Asunto", "Hola {PatientName}.");

    private readonly ICommandHandler<CreateReminderTemplateCommand, ReminderTemplateDto> _create =
        Substitute.For<ICommandHandler<CreateReminderTemplateCommand, ReminderTemplateDto>>();
    private readonly ICommandHandler<UpdateReminderTemplateCommand, ReminderTemplateDto> _update =
        Substitute.For<ICommandHandler<UpdateReminderTemplateCommand, ReminderTemplateDto>>();
    private readonly ICommandHandler<DeactivateReminderTemplateCommand, Unit> _deactivate =
        Substitute.For<ICommandHandler<DeactivateReminderTemplateCommand, Unit>>();

    [Fact]
    public async Task Create_pasa_el_comando_y_el_token_y_responde_201_con_la_plantilla()
    {
        using var cancellation = new CancellationTokenSource();
        _create.HandleAsync(Arg.Any<CreateReminderTemplateCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReminderTemplateDto>.Success(Dto));

        var result = await new DoctorReminderTemplatesController().Create(Request, _create, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        response.Value.ShouldBe(Response);
        await _create.Received(1).HandleAsync(
            new CreateReminderTemplateCommand("Indicaciones", "Asunto", "Hola {PatientName}."), cancellation.Token);
    }

    [Fact]
    public async Task Update_pasa_el_id_y_los_datos_y_responde_200_con_la_plantilla()
    {
        _update.HandleAsync(Arg.Any<UpdateReminderTemplateCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReminderTemplateDto>.Success(Dto));

        var result = await new DoctorReminderTemplatesController().Update(Id, Request, _update, CancellationToken.None);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(Response);
        await _update.Received(1).HandleAsync(
            new UpdateReminderTemplateCommand(Id, "Indicaciones", "Asunto", "Hola {PatientName}."), CancellationToken.None);
    }

    [Fact]
    public async Task Deactivate_pasa_el_id_y_responde_204()
    {
        _deactivate.HandleAsync(Arg.Any<DeactivateReminderTemplateCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Success(Unit.Value));

        var result = await new DoctorReminderTemplatesController().Deactivate(Id, _deactivate, CancellationToken.None);

        result.ShouldBeOfType<NoContentResult>();
        await _deactivate.Received(1).HandleAsync(new DeactivateReminderTemplateCommand(Id), CancellationToken.None);
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [ReminderTemplateErrors.UnknownVariable("Paciente"), 400];
        yield return [ReminderTemplateErrors.DoctorOnly, 403];
        yield return [ReminderTemplateErrors.GlobalReadOnly, 403];
        yield return [ReminderTemplateErrors.NotFound, 404];
        yield return [ActivationErrors.AlreadyInactive("ReminderTemplate"), 409];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Los_errores_salen_con_ErrorResult(Error error, int status)
    {
        _update.HandleAsync(Arg.Any<UpdateReminderTemplateCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ReminderTemplateDto>.Failure(error));
        _deactivate.HandleAsync(Arg.Any<DeactivateReminderTemplateCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<Unit>.Failure(error));
        var controller = new DoctorReminderTemplatesController();

        AssertProblem(await controller.Update(Id, Request, _update, CancellationToken.None), error, status);
        AssertProblem(await controller.Deactivate(Id, _deactivate, CancellationToken.None), error, status);
    }

    [Theory]
    [InlineData(nameof(DoctorReminderTemplatesController.Create), "POST", null)]
    [InlineData(nameof(DoctorReminderTemplatesController.Update), "PUT", "{id:int}")]
    [InlineData(nameof(DoctorReminderTemplatesController.Deactivate), "PATCH", "{id:int}/deactivate")]
    public void Las_acciones_son_solo_para_medicos(string action, string verb, string? template)
    {
        typeof(DoctorReminderTemplatesController).GetCustomAttribute<RouteAttribute>()!.Template
            .ShouldBe("api/doctors/me/reminder-templates");
        var method = typeof(DoctorReminderTemplatesController).GetMethod(action)!;
        var http = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        http.HttpMethods.ShouldBe(new[] { verb });
        http.Template.ShouldBe(template);
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
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
