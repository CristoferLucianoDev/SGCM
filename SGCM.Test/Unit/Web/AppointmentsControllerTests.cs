using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Commands.RequestAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AppointmentsControllerTests
{
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 9, 0, 0, TimeSpan.FromHours(-4));

    private readonly IQueryHandler<GetDoctorAssignmentQuery, DoctorAssignmentDto> _assignmentHandler =
        Substitute.For<IQueryHandler<GetDoctorAssignmentQuery, DoctorAssignmentDto>>();

    private readonly ICommandHandler<RequestAppointmentCommand, RequestedAppointmentDto> _requestHandler =
        Substitute.For<ICommandHandler<RequestAppointmentCommand, RequestedAppointmentDto>>();

    [Fact]
    public async Task AssignDoctor_pasa_la_consulta_y_el_token_al_handler_y_responde_200_con_el_medico_y_su_tarifa()
    {
        using var cancellation = new CancellationTokenSource();
        _assignmentHandler.HandleAsync(Arg.Any<GetDoctorAssignmentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<DoctorAssignmentDto>.Success(new DoctorAssignmentDto(7, "Luis Pérez", 1500m)));

        var result = await new AppointmentsController().AssignDoctor(
            new DoctorAssignmentRequest(5, StartsAt, 3, 4), _assignmentHandler, cancellation.Token);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(new DoctorAssignmentResponse(7, "Luis Pérez", 1500m));
        await _assignmentHandler.Received(1).HandleAsync(new GetDoctorAssignmentQuery(5, StartsAt, 3, 4), cancellation.Token);
    }

    public static IEnumerable<object[]> AssignmentErrors()
    {
        yield return [AppointmentErrors.TooSoon, 400];
        yield return [AppointmentErrors.BeyondHorizon, 400];
        yield return [AppointmentErrors.NotOnHour, 400];
        yield return [AppointmentErrors.PatientOnly, 403];
        yield return [AppointmentErrors.SlotUnavailable, 409];
    }

    [Theory]
    [MemberData(nameof(AssignmentErrors))]
    public async Task AssignDoctor_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _assignmentHandler.HandleAsync(Arg.Any<GetDoctorAssignmentQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<DoctorAssignmentDto>.Failure(error));

        var result = await new AppointmentsController().AssignDoctor(
            new DoctorAssignmentRequest(5, StartsAt), _assignmentHandler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void AssignDoctor_es_un_GET_solo_para_pacientes_con_sus_respuestas()
    {
        var type = typeof(AppointmentsController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/appointments");
        type.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();

        var method = type.GetMethod(nameof(AppointmentsController.AssignDoctor))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBe("doctor-assignment");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Patient));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        method.GetParameters()[0].GetCustomAttribute<FromQueryAttribute>().ShouldNotBeNull();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 200, 400, 401, 403, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(typeof(DoctorAssignmentResponse));
        responses.Single(value => value.StatusCode == 409).Type.ShouldBe(typeof(ProblemDetails));
    }

    [Fact]
    public async Task Create_pasa_el_comando_y_el_token_al_handler_y_responde_201_con_la_cita()
    {
        using var cancellation = new CancellationTokenSource();
        var endsAt = StartsAt.AddHours(2);
        _requestHandler.HandleAsync(Arg.Any<RequestAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<RequestedAppointmentDto>.Success(new RequestedAppointmentDto(99, 8, "Ana Gómez", 1800m, StartsAt, endsAt, true)));

        var result = await new AppointmentsController().Create(
            new RequestAppointmentRequest(5, StartsAt, AssignedDoctorId: 7, ReasonForVisit: "Control"), _requestHandler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(201);
        response.Value.ShouldBe(new RequestAppointmentResponse(99, 8, "Ana Gómez", 1800m, StartsAt, endsAt, true));
        await _requestHandler.Received(1).HandleAsync(
            new RequestAppointmentCommand(5, StartsAt, AssignedDoctorId: 7, ReasonForVisit: "Control"), cancellation.Token);
    }

    public static IEnumerable<object[]> RequestErrors()
    {
        yield return [AppointmentErrors.TooSoon, 400];
        yield return [AppointmentErrors.DoctorNotInSpecialty, 400];
        yield return [AppointmentErrors.PatientOnly, 403];
        yield return [PatientErrors.ProfileNotFound, 404];
        yield return [AppointmentErrors.SlotUnavailable, 409];
        yield return [AppointmentErrors.PatientOverlap, 409];
    }

    [Theory]
    [MemberData(nameof(RequestErrors))]
    public async Task Create_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _requestHandler.HandleAsync(Arg.Any<RequestAppointmentCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<RequestedAppointmentDto>.Failure(error));

        var result = await new AppointmentsController().Create(
            new RequestAppointmentRequest(5, StartsAt), _requestHandler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Create_es_un_POST_solo_para_pacientes_con_sus_respuestas()
    {
        var method = typeof(AppointmentsController).GetMethod(nameof(AppointmentsController.Create))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBeNull();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Patient));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 201, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 201).Type.ShouldBe(typeof(RequestAppointmentResponse));
    }
}
