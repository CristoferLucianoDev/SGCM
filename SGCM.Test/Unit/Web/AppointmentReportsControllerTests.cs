using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AppointmentReportsControllerTests
{
    private static readonly GenerateAppointmentReportRequest Request =
        new(AppointmentReportType.All, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));

    private readonly ICommandHandler<GenerateAppointmentReportCommand, AppointmentReport> _handler =
        Substitute.For<ICommandHandler<GenerateAppointmentReportCommand, AppointmentReport>>();

    [Fact]
    public async Task Generar_mapea_el_comando_y_responde_200_con_el_reporte()
    {
        using var cancellation = new CancellationTokenSource();
        var report = new AppointmentReport(
            7, new Dictionary<AppointmentStatus, int>(), new PagedResult<AppointmentReportRow>([], 1, 20, 0));
        _handler.HandleAsync(Arg.Any<GenerateAppointmentReportCommand>(), cancellation.Token)
            .Returns(Result<AppointmentReport>.Success(report));

        var result = await new AppointmentReportsController().Generate(Request, _handler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AppointmentReportResponse>();
        response.TotalCount.ShouldBe(0);
        await _handler.Received(1).HandleAsync(
            new GenerateAppointmentReportCommand(new ReportCriteria(ReportType.All, Request.From!.Value, Request.To!.Value)),
            cancellation.Token);
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [ReportErrors.DoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Generar_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        _handler.HandleAsync(Arg.Any<GenerateAppointmentReportCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Failure(error));

        var result = await new AppointmentReportsController().Generate(Request, _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Contrato_es_un_POST_exclusivo_del_rol_Doctor_que_no_recibe_el_medico()
    {
        var type = typeof(AppointmentReportsController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/doctors/me/appointment-reports");
        type.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        var method = type.GetMethod(nameof(AppointmentReportsController.Generate))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBeNull();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        typeof(GenerateAppointmentReportRequest).GetProperties().Select(property => property.Name)
            .ShouldNotContain("DoctorId");
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(typeof(AppointmentReportResponse));
    }

    [Fact]
    public void El_tipo_es_obligatorio_en_la_peticion()
    {
        typeof(GenerateAppointmentReportRequest).GetConstructors().ShouldHaveSingleItem()
            .GetParameters().Single(parameter => parameter.Name == nameof(GenerateAppointmentReportRequest.Type))
            .GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
    }

    [Fact]
    public void Los_tipos_estados_y_orden_se_leen_por_nombre_sin_distinguir_mayusculas()
    {
        const string json = """
            {"type":"pending","from":"2027-01-01","to":"2027-01-31","status":"CONFIRMED",
             "sortBy":"patient","sortDirection":"descending"}
            """;

        var request = JsonSerializer.Deserialize<GenerateAppointmentReportRequest>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        request.Type.ShouldBe(AppointmentReportType.Pending);
        request.Status.ShouldBe(AppointmentStatusFilter.Confirmed);
        request.SortBy.ShouldBe(AppointmentReportSortField.Patient);
        request.SortDirection.ShouldBe(AppointmentReportSortDirection.Descending);
        request.PageSize.ShouldBe(GenerateAppointmentReportCommand.DefaultPageSize);
    }
}
