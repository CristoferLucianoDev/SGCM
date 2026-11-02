using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Test.Common.Mothers;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Identity;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public class PatientsControllerTests
{
    private const string ClientUserAgent = "Chrome en Windows";
    private const string ClientIp = "203.0.113.10";

    private readonly ICommandHandler<CompletePatientProfileCommand, SessionTokens> _handler =
        Substitute.For<ICommandHandler<CompletePatientProfileCommand, SessionTokens>>();

    [Fact]
    public async Task CompleteProfile_mapea_la_solicitud_con_dispositivo_e_ip_y_devuelve_los_tokens()
    {
        using var cancellation = new CancellationTokenSource();
        var tokens = new SessionTokens(
            new AccessToken("access-token", UserMother.Now.AddMinutes(15)), "refresh-token", UserMother.Now.AddDays(7), null);
        _handler.HandleAsync(Arg.Any<CompletePatientProfileCommand>(), cancellation.Token).Returns(tokens);

        var result = await CreateController().CompleteProfile(Request(), _handler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LoginResponse>();
        response.RequiresTwoFactor.ShouldBeFalse();
        response.AccessToken.ShouldBe("access-token");
        response.RefreshToken.ShouldBe("refresh-token");
        response.PendingStep.ShouldBeNull();
        await _handler.Received(1).HandleAsync(
            CompletePatientProfileCommandMother.WithRequiredData() with { DeviceName = ClientUserAgent, IpAddress = ClientIp },
            cancellation.Token);
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 409)]
    public async Task CompleteProfile_devuelve_errores_mediante_ApiControllerBase(bool duplicate, int expectedStatus)
    {
        var error = duplicate ? PatientErrors.ProfileAlreadyExists : PatientErrors.InvalidBirthDate;
        _handler.HandleAsync(Arg.Any<CompletePatientProfileCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<SessionTokens>.Failure(error));

        var result = await CreateController().CompleteProfile(Request(), _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(expectedStatus);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void CompleteProfile_es_solo_para_pacientes_y_admite_el_paso_pendiente_del_perfil()
    {
        var method = typeof(PatientsController).GetMethod(nameof(PatientsController.CompleteProfile))!;

        method.GetCustomAttribute<AuthorizeAttribute>()!.Roles.ShouldBe(nameof(UserRole.Patient));
        method.GetCustomAttribute<AllowPendingStepAttribute>()!.Step.ShouldBe(PendingStep.PatientProfile);
    }

    private static CompletePatientProfileRequest Request() => new(
        PatientMother.DefaultFirstName,
        PatientMother.DefaultLastName,
        PatientMother.DefaultBirthDate,
        PatientMother.DefaultPhoneNumber,
        Address: null,
        EmergencyContactName: null,
        EmergencyContactPhone: null,
        InsuranceProviderId: null,
        PolicyNumber: null);

    private static PatientsController CreateController()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers.UserAgent = ClientUserAgent;
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);

        return new PatientsController { ControllerContext = new ControllerContext { HttpContext = httpContext } };
    }
}
