using NSubstitute;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetAppointmentReportQueryHandlerTests
{
    private const int PageNumber = 3;
    private const int PageSize = 10;

    private readonly IAppointmentReportBuilder _builder = Substitute.For<IAppointmentReportBuilder>();

    [Fact]
    public async Task Construye_la_pagina_pedida_con_los_filtros_y_el_orden_de_los_criterios()
    {
        var criteria = ReportCriteriaMother.Valid() with
        {
            Status = AppointmentStatus.Cancelled,
            PatientName = "Ana",
            SpecialtyId = 4,
            SortBy = ReportSortField.Status,
            SortDirection = ReportSortDirection.Descending
        };
        var report = Report();
        GivenReport(report);
        using var cancellation = new CancellationTokenSource();

        var result = await CreateHandler().HandleAsync(
            new GetAppointmentReportQuery(criteria, PageNumber, PageSize), cancellation.Token);

        result.Value.ShouldBeSameAs(report);
        await _builder.Received(1).BuildPageAsync(criteria, PageNumber, PageSize, cancellation.Token);
        await _builder.DidNotReceiveWithAnyArgs().BuildAllAsync(default!, default);
    }

    [Fact]
    public async Task Una_consulta_invalida_devuelve_el_error_de_validacion_sin_construir_el_reporte()
    {
        var result = await CreateHandler().HandleAsync(
            new GetAppointmentReportQuery(ReportCriteriaMother.Valid(), 0), CancellationToken.None);

        result.Error.Code.ShouldBe("Report.InvalidPageNumber");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        await _builder.DidNotReceiveWithAnyArgs().BuildPageAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Devuelve_el_error_del_builder()
    {
        _builder.BuildPageAsync(Arg.Any<ReportCriteria>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Failure(ReportErrors.DoctorOnly));

        var result = await CreateHandler().HandleAsync(
            new GetAppointmentReportQuery(ReportCriteriaMother.Valid()), CancellationToken.None);

        result.Error.ShouldBe(ReportErrors.DoctorOnly);
    }

    [Fact]
    public void No_depende_del_registro_de_reportes_porque_filtrar_no_genera_un_registro()
    {
        typeof(GetAppointmentReportQueryHandler).GetConstructors().ShouldHaveSingleItem()
            .GetParameters().Select(parameter => parameter.ParameterType)
            .ShouldNotContain(typeof(IAppointmentReportLogRepository));
    }

    private GetAppointmentReportQueryHandler CreateHandler() =>
        new(_builder, new GetAppointmentReportQueryValidator());

    private void GivenReport(AppointmentReport report) =>
        _builder.BuildPageAsync(Arg.Any<ReportCriteria>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Success(report));

    private static AppointmentReport Report() =>
        new(7,
            Enum.GetValues<AppointmentStatus>().ToDictionary(status => status, _ => 0),
            new PagedResult<AppointmentReportRow>([], PageNumber, PageSize, 0));
}
