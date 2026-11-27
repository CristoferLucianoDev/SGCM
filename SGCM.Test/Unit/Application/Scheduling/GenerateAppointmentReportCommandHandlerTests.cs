using NSubstitute;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GenerateAppointmentReportCommandHandlerTests
{
    private const int DoctorId = 7;
    private const int PageSize = 10;
    private const int TotalRows = 35;

    private static readonly DateTimeOffset Now = new(2027, 1, 12, 13, 30, 0, TimeSpan.Zero);

    private readonly IAppointmentReportBuilder _builder = Substitute.For<IAppointmentReportBuilder>();
    private readonly IAppointmentReportLogRepository _logRepository = Substitute.For<IAppointmentReportLogRepository>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = Now };

    [Fact]
    public async Task Construye_la_primera_pagina_con_los_criterios_y_el_tamano_pedido()
    {
        var criteria = ReportCriteriaMother.Valid() with { Type = ReportType.Pending };
        GivenReport(Report());
        using var cancellation = new CancellationTokenSource();

        await CreateHandler().HandleAsync(new GenerateAppointmentReportCommand(criteria, PageSize), cancellation.Token);

        await _builder.Received(1).BuildPageAsync(criteria, 1, PageSize, cancellation.Token);
        await _builder.DidNotReceiveWithAnyArgs().BuildAllAsync(default!, default);
    }

    [Fact]
    public async Task Devuelve_el_reporte_construido()
    {
        var report = Report();
        GivenReport(report);

        var result = await CreateHandler().HandleAsync(
            new GenerateAppointmentReportCommand(ReportCriteriaMother.Valid(), PageSize), CancellationToken.None);

        result.Value.ShouldBeSameAs(report);
    }

    [Fact]
    public async Task Registra_la_generacion_con_el_medico_los_criterios_el_total_de_filas_y_la_hora_del_reloj()
    {
        var criteria = ReportCriteriaMother.Valid() with { Type = ReportType.Cancelled, PatientName = "Ana" };
        GivenReport(Report());

        await CreateHandler().HandleAsync(new GenerateAppointmentReportCommand(criteria, PageSize), CancellationToken.None);

        _logRepository.Received(1).Add(Arg.Is<AppointmentReportLog>(log =>
            log.DoctorId == DoctorId
            && log.Action == ReportAction.Generate
            && log.ReportType == ReportType.Cancelled
            && log.Filters == criteria.ToJson()
            && log.ExportFormat == null
            && log.RowCount == TotalRows
            && log.GeneratedAt == Now));
    }

    [Fact]
    public async Task Un_reporte_vacio_tambien_se_registra()
    {
        GivenReport(Report(totalRows: 0));

        var result = await CreateHandler().HandleAsync(
            new GenerateAppointmentReportCommand(ReportCriteriaMother.Valid()), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _logRepository.Received(1).Add(Arg.Is<AppointmentReportLog>(log => log.RowCount == 0));
    }

    [Fact]
    public async Task Si_el_reporte_falla_devuelve_el_error_y_no_registra_nada()
    {
        _builder.BuildPageAsync(Arg.Any<ReportCriteria>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Failure(ReportErrors.DoctorOnly));

        var result = await CreateHandler().HandleAsync(
            new GenerateAppointmentReportCommand(ReportCriteriaMother.Valid()), CancellationToken.None);

        result.Error.ShouldBe(ReportErrors.DoctorOnly);
        _logRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    private GenerateAppointmentReportCommandHandler CreateHandler() => new(_builder, _logRepository, _clock);

    private void GivenReport(AppointmentReport report) =>
        _builder.BuildPageAsync(Arg.Any<ReportCriteria>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Success(report));

    private static AppointmentReport Report(int totalRows = TotalRows) =>
        new(DoctorId,
            Enum.GetValues<AppointmentStatus>().ToDictionary(status => status, _ => 0),
            new PagedResult<AppointmentReportRow>([], 1, PageSize, totalRows));
}
