using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;
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

    [Fact]
    public async Task Filtrar_mapea_la_consulta_y_responde_200_con_el_reporte()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<IQueryHandler<GetAppointmentReportQuery, AppointmentReport>>();
        var report = new AppointmentReport(
            7, new Dictionary<AppointmentStatus, int> { [AppointmentStatus.Cancelled] = 1 },
            new PagedResult<AppointmentReportRow>([], 2, 5, 1));
        handler.HandleAsync(Arg.Any<GetAppointmentReportQuery>(), cancellation.Token)
            .Returns(Result<AppointmentReport>.Success(report));
        var request = new GetAppointmentReportRequest(
            AppointmentReportType.Cancelled, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), PageNumber: 2, PageSize: 5);

        var result = await new AppointmentReportsController().Get(request, handler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AppointmentReportResponse>();
        response.PageNumber.ShouldBe(2);
        response.TotalCount.ShouldBe(1);
        await handler.Received(1).HandleAsync(
            new GetAppointmentReportQuery(
                new ReportCriteria(ReportType.Cancelled, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)), 2, 5),
            cancellation.Token);
    }

    public static IEnumerable<object[]> QueryErrors()
    {
        yield return [Error.Validation("Report.InvalidPageNumber", "El número de página debe ser mayor o igual a 1."), 400];
        yield return [ReportErrors.DoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
    }

    [Theory]
    [MemberData(nameof(QueryErrors))]
    public async Task Filtrar_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        var handler = Substitute.For<IQueryHandler<GetAppointmentReportQuery, AppointmentReport>>();
        handler.HandleAsync(Arg.Any<GetAppointmentReportQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Failure(error));
        var request = new GetAppointmentReportRequest(
            AppointmentReportType.All, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));

        var result = await new AppointmentReportsController().Get(request, handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Filtrar_es_un_GET_exclusivo_del_rol_Doctor_con_los_parametros_en_la_query()
    {
        var method = typeof(AppointmentReportsController).GetMethod(nameof(AppointmentReportsController.Get))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBeNull();
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetParameters()[0].GetCustomAttribute<FromQueryAttribute>().ShouldNotBeNull();
        typeof(GetAppointmentReportRequest).GetProperties().Select(property => property.Name).ShouldNotContain("DoctorId");
        typeof(GetAppointmentReportRequest).GetConstructors().ShouldHaveSingleItem()
            .GetParameters().Single(parameter => parameter.Name == nameof(GetAppointmentReportRequest.Type))
            .GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(typeof(AppointmentReportResponse));
    }

    [Fact]
    public async Task Exportar_mapea_el_comando_y_devuelve_el_archivo_con_su_nombre_y_tipo()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = Substitute.For<ICommandHandler<ExportAppointmentReportCommand, ExportedFile>>();
        var file = new ExportedFile("reporte-citas-20270112-0930.pdf", "application/pdf", [1, 2, 3]);
        handler.HandleAsync(Arg.Any<ExportAppointmentReportCommand>(), cancellation.Token)
            .Returns(Result<ExportedFile>.Success(file));
        var request = new ExportAppointmentReportRequest(
            AppointmentReportFormat.Pdf, AppointmentReportType.All, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));

        var result = await new AppointmentReportsController().Export(request, handler, cancellation.Token);

        var content = result.ShouldBeOfType<FileContentResult>();
        content.FileContents.ShouldBe(file.Content);
        content.ContentType.ShouldBe(file.ContentType);
        content.FileDownloadName.ShouldBe(file.FileName);
        await handler.Received(1).HandleAsync(
            new ExportAppointmentReportCommand(
                new ReportCriteria(ReportType.All, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31)), ReportFormat.Pdf),
            cancellation.Token);
    }

    public static IEnumerable<object[]> ExportErrors()
    {
        yield return [ReportErrors.EmptyReport, 400];
        yield return [ReportErrors.DoctorOnly, 403];
        yield return [DoctorErrors.NotFound, 404];
    }

    [Theory]
    [MemberData(nameof(ExportErrors))]
    public async Task Exportar_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        var handler = Substitute.For<ICommandHandler<ExportAppointmentReportCommand, ExportedFile>>();
        handler.HandleAsync(Arg.Any<ExportAppointmentReportCommand>(), Arg.Any<CancellationToken>())
            .Returns(Result<ExportedFile>.Failure(error));
        var request = new ExportAppointmentReportRequest(
            AppointmentReportFormat.Xlsx, AppointmentReportType.All, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31));

        var result = await new AppointmentReportsController().Export(request, handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Fact]
    public void Exportar_es_un_POST_en_export_exclusivo_del_rol_Doctor_con_formato_y_tipo_obligatorios()
    {
        var method = typeof(AppointmentReportsController).GetMethod(nameof(AppointmentReportsController.Export))!;
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "POST" });
        verb.Template.ShouldBe("export");
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        var parameters = typeof(ExportAppointmentReportRequest).GetConstructors().ShouldHaveSingleItem().GetParameters();
        parameters.Single(parameter => parameter.Name == "Format").GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
        parameters.Single(parameter => parameter.Name == "Type").GetCustomAttribute<RequiredAttribute>().ShouldNotBeNull();
        typeof(ExportAppointmentReportRequest).GetProperties().Select(property => property.Name).ShouldNotContain("DoctorId");
        method.GetCustomAttributes<ProducesResponseTypeAttribute>().Select(value => value.StatusCode)
            .ShouldBe(new[] { 200, 400, 401, 403, 404 }, ignoreOrder: true);
    }
}
