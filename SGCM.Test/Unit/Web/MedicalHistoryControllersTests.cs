using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;
using SGCM.Web.Api.Controllers.Clinical;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Clinical;
using Shouldly;

namespace SGCM.Test.Unit.Web;

/// <summary>Pruebas de los controladores del historial médico del paciente y del médico.</summary>
public sealed class MedicalHistoryControllersTests
{
    private const int PatientId = 20;
    private const string ClientIp = "203.0.113.10";

    private static readonly MedicalHistoryDto EmptyHistory = new(PatientId, []);

    private readonly IQueryHandler<GetMedicalHistoryQuery, MedicalHistoryDto> _handler =
        Substitute.For<IQueryHandler<GetMedicalHistoryQuery, MedicalHistoryDto>>();

    [Fact]
    public async Task El_paciente_consulta_su_historial_sin_paciente_en_la_consulta_y_con_su_ip()
    {
        using var cancellation = new CancellationTokenSource();
        Returns(Result<MedicalHistoryDto>.Success(EmptyHistory));

        var result = await WithContext(new PatientMedicalHistoryController()).Get(_handler, cancellation.Token);

        AssertEmptyHistory(result);
        await _handler.Received(1).HandleAsync(new GetMedicalHistoryQuery(null, ClientIp), cancellation.Token);
    }

    [Fact]
    public async Task El_medico_consulta_el_historial_del_paciente_de_la_ruta_con_su_ip()
    {
        using var cancellation = new CancellationTokenSource();
        Returns(Result<MedicalHistoryDto>.Success(EmptyHistory));

        var result = await WithContext(new DoctorPatientMedicalHistoryController())
            .Get(PatientId, _handler, cancellation.Token);

        AssertEmptyHistory(result);
        await _handler.Received(1).HandleAsync(new GetMedicalHistoryQuery(PatientId, ClientIp), cancellation.Token);
    }

    public static IEnumerable<object[]> PatientHistoryErrors()
    {
        yield return [MedicalHistoryErrors.PatientOnly, 403];
        yield return [PatientErrors.ProfileNotFound, 404];
    }

    public static IEnumerable<object[]> DoctorHistoryErrors()
    {
        yield return [Error.Validation("MedicalHistory.InvalidPatientId", "Id no válido."), 400];
        yield return [MedicalHistoryErrors.DoctorOnly, 403];
        yield return [MedicalHistoryErrors.NoAppointmentWithPatient, 403];
        yield return [DoctorErrors.NotFound, 404];
    }

    [Theory]
    [MemberData(nameof(PatientHistoryErrors))]
    public async Task El_historial_del_paciente_utiliza_ErrorResult(Error error, int status)
    {
        Returns(Result<MedicalHistoryDto>.Failure(error));

        AssertProblem(await WithContext(new PatientMedicalHistoryController()).Get(_handler, CancellationToken.None),
            error, status);
    }

    [Theory]
    [MemberData(nameof(DoctorHistoryErrors))]
    public async Task El_historial_del_medico_utiliza_ErrorResult(Error error, int status)
    {
        Returns(Result<MedicalHistoryDto>.Failure(error));

        AssertProblem(
            await WithContext(new DoctorPatientMedicalHistoryController()).Get(PatientId, _handler, CancellationToken.None),
            error, status);
    }

    [Fact]
    public void El_historial_del_paciente_es_un_GET_solo_para_pacientes()
    {
        typeof(PatientMedicalHistoryController).GetCustomAttribute<RouteAttribute>()!.Template
            .ShouldBe("api/patients/me/medical-history");
        var method = AssertGet(typeof(PatientMedicalHistoryController), template: null, UserRole.Patient);
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 401, 403, 404 }, ignoreOrder: true);
    }

    [Fact]
    public void El_historial_del_medico_es_un_GET_solo_para_medicos()
    {
        typeof(DoctorPatientMedicalHistoryController).GetCustomAttribute<RouteAttribute>()!.Template
            .ShouldBe("api/doctors/me/patients");
        var method = AssertGet(
            typeof(DoctorPatientMedicalHistoryController), "{patientId:int}/medical-history", UserRole.Doctor);
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
    }

    // La respuesta trae listas, que un record compara por referencia: se comparan sus valores.
    private static void AssertEmptyHistory(IActionResult result)
    {
        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<MedicalHistoryResponse>();
        response.PatientId.ShouldBe(PatientId);
        response.Entries.ShouldBeEmpty();
    }

    private void Returns(Result<MedicalHistoryDto> result) =>
        _handler.HandleAsync(Arg.Any<GetMedicalHistoryQuery>(), Arg.Any<CancellationToken>()).Returns(result);

    private static MethodInfo AssertGet(Type controller, string? template, UserRole role)
    {
        var method = controller.GetMethod("Get")!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBe(template);
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(role.ToString());
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Single(value => value.StatusCode == 200).Type
            .ShouldBe(typeof(MedicalHistoryResponse));
        return method;
    }

    private static void AssertProblem(IActionResult result, Error error, int status)
    {
        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    private static T WithContext<T>(T controller) where T : ControllerBase
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ClientIp);
        controller.ControllerContext = new ControllerContext { HttpContext = context };
        return controller;
    }
}
