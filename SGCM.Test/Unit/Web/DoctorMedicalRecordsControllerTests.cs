using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Api.Controllers.Clinical;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Clinical;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class DoctorMedicalRecordsControllerTests
{
    private const int AppointmentId = 99;
    private const string ClientIp = "203.0.113.10";

    private static readonly DateTimeOffset CreatedAt = new(2027, 1, 12, 13, 30, 0, TimeSpan.Zero);

    private readonly ICommandHandler<CreateMedicalRecordVersionCommand, MedicalRecordVersionResult> _handler =
        Substitute.For<ICommandHandler<CreateMedicalRecordVersionCommand, MedicalRecordVersionResult>>();

    [Fact]
    public async Task CreateVersion_pasa_el_comando_con_la_ip_y_el_token_y_responde_201_con_la_version()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<CreateMedicalRecordVersionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<MedicalRecordVersionResult>.Success(
                new MedicalRecordVersionResult(501, AppointmentId, "Faringitis", "Reposo", null, CreatedAt, false)));

        var result = await Controller().CreateVersion(
            AppointmentId, new CreateMedicalRecordVersionRequest("Faringitis", "Reposo"), _handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(StatusCodes.Status201Created);
        response.Value.ShouldBe(
            new MedicalRecordVersionResponse(501, AppointmentId, "Faringitis", "Reposo", null, CreatedAt, false));
        await _handler.Received(1).HandleAsync(
            new CreateMedicalRecordVersionCommand(AppointmentId, "Faringitis", "Reposo", null, ClientIp),
            cancellation.Token);
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [MedicalRecordErrors.Empty, 400];
        yield return [MedicalRecordErrors.NotesTooLong, 400];
        yield return [AppointmentErrors.ClinicalRecordDoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AppointmentErrors.NotFound, 404];
        yield return [AppointmentErrors.ClinicalRecordInvalidStatus, 409];
        yield return [AppointmentErrors.ClinicalRecordNotStarted, 409];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task CreateVersion_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _handler.HandleAsync(Arg.Any<CreateMedicalRecordVersionCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<MedicalRecordVersionResult>.Failure(error));

        var result = await Controller().CreateVersion(
            AppointmentId, new CreateMedicalRecordVersionRequest(), _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void CreateVersion_es_un_POST_solo_para_medicos_con_sus_respuestas()
    {
        typeof(DoctorMedicalRecordsController).GetCustomAttribute<RouteAttribute>()!.Template
            .ShouldBe("api/doctors/me/appointments");
        var method = typeof(DoctorMedicalRecordsController).GetMethod(nameof(DoctorMedicalRecordsController.CreateVersion))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBe("{id:int}/medical-record");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 201, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 201).Type.ShouldBe(typeof(MedicalRecordVersionResponse));
        responses.Single(value => value.StatusCode == 400).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 404).Type.ShouldBe(typeof(ProblemDetails));
        responses.Single(value => value.StatusCode == 409).Type.ShouldBe(typeof(ProblemDetails));
    }

    private static DoctorMedicalRecordsController Controller()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);

        return new DoctorMedicalRecordsController { ControllerContext = new ControllerContext { HttpContext = context } };
    }
}
