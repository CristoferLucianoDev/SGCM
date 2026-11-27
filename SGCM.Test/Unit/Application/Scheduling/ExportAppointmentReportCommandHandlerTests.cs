using NSubstitute;
using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class ExportAppointmentReportCommandHandlerTests
{
    private const int DoctorId = 7;
    private const string TimeZone = "America/Santo_Domingo";

    // 13:30 UTC son las 09:30 en Santo Domingo.
    private static readonly DateTimeOffset Now = new(2027, 1, 12, 13, 30, 0, TimeSpan.Zero);

    private readonly IAppointmentReportBuilder _builder = Substitute.For<IAppointmentReportBuilder>();
    private readonly IAppointmentReportLogRepository _logRepository = Substitute.For<IAppointmentReportLogRepository>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly Dictionary<ReportFormat, IReportExporter> _exporters = Enum.GetValues<ReportFormat>()
        .ToDictionary(format => format, Exporter);

    [Theory]
    [InlineData(ReportFormat.Pdf)]
    [InlineData(ReportFormat.Xlsx)]
    [InlineData(ReportFormat.Docx)]
    public async Task Exporta_con_el_exportador_del_formato_pedido_y_devuelve_su_archivo(ReportFormat format)
    {
        GivenReport(Report(rows: 2));

        var result = await CreateHandler().HandleAsync(Command(format), CancellationToken.None);

        result.Value.FileName.ShouldBe($"archivo.{format}");
        foreach (var exporter in _exporters.Where(pair => pair.Key != format).Select(pair => pair.Value))
            exporter.DidNotReceiveWithAnyArgs().Export(default!);
    }

    [Fact]
    public async Task Construye_el_reporte_completo_con_los_criterios_para_que_coincida_con_la_pantalla()
    {
        var criteria = ReportCriteriaMother.Valid() with { SortBy = ReportSortField.Patient };
        GivenReport(Report(rows: 2));
        using var cancellation = new CancellationTokenSource();

        await CreateHandler().HandleAsync(new ExportAppointmentReportCommand(criteria, ReportFormat.Pdf), cancellation.Token);

        await _builder.Received(1).BuildAllAsync(criteria, cancellation.Token);
        await _builder.DidNotReceiveWithAnyArgs().BuildPageAsync(default!, default, default, default);
    }

    [Fact]
    public async Task Entrega_al_exportador_todas_las_filas_y_la_hora_local_de_generacion()
    {
        GivenReport(Report(rows: 2));

        await CreateHandler().HandleAsync(Command(ReportFormat.Xlsx), CancellationToken.None);

        _exporters[ReportFormat.Xlsx].Received(1).Export(Arg.Is<ReportDocument>(document =>
            document.Rows.Count == 2
            && document.GeneratedAt == new DateTimeOffset(2027, 1, 12, 9, 30, 0, TimeSpan.FromHours(-4))));
    }

    [Theory]
    [InlineData(ReportFormat.Pdf, "pdf")]
    [InlineData(ReportFormat.Xlsx, "xlsx")]
    [InlineData(ReportFormat.Docx, "docx")]
    public async Task Registra_la_exportacion_con_su_formato_la_cantidad_de_filas_y_la_hora(ReportFormat format, string logFormat)
    {
        GivenReport(Report(rows: 2));

        await CreateHandler().HandleAsync(Command(format), CancellationToken.None);

        _logRepository.Received(1).Add(Arg.Is<AppointmentReportLog>(log =>
            log.DoctorId == DoctorId
            && log.Action == ReportAction.Export
            && log.ExportFormat == logFormat
            && log.RowCount == 2
            && log.GeneratedAt == Now));
    }

    [Fact]
    public async Task Un_reporte_vacio_se_rechaza_sin_exportar_ni_registrar()
    {
        GivenReport(Report(rows: 0));

        var result = await CreateHandler().HandleAsync(Command(ReportFormat.Pdf), CancellationToken.None);

        result.Error.ShouldBe(ReportErrors.EmptyReport);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _exporters[ReportFormat.Pdf].DidNotReceiveWithAnyArgs().Export(default!);
        _logRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task Si_el_reporte_falla_devuelve_el_error_sin_exportar_ni_registrar()
    {
        _builder.BuildAllAsync(Arg.Any<ReportCriteria>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Failure(ReportErrors.DoctorOnly));

        var result = await CreateHandler().HandleAsync(Command(ReportFormat.Pdf), CancellationToken.None);

        result.Error.ShouldBe(ReportErrors.DoctorOnly);
        _logRepository.DidNotReceiveWithAnyArgs().Add(default!);
    }

    private ExportAppointmentReportCommandHandler CreateHandler() =>
        new(_builder, _exporters.Values, _logRepository, _clock, BusinessCalendar.ForTimeZone(TimeZone));

    private static ExportAppointmentReportCommand Command(ReportFormat format) =>
        new(ReportCriteriaMother.Valid(), format);

    private void GivenReport(AppointmentReport report) =>
        _builder.BuildAllAsync(Arg.Any<ReportCriteria>(), Arg.Any<CancellationToken>())
            .Returns(Result<AppointmentReport>.Success(report));

    private static IReportExporter Exporter(ReportFormat format)
    {
        var exporter = Substitute.For<IReportExporter>();
        exporter.Format.Returns(format);
        exporter.Export(Arg.Any<ReportDocument>()).Returns(new ExportedFile($"archivo.{format}", "tipo", [1]));
        return exporter;
    }

    private static AppointmentReport Report(int rows) =>
        new(DoctorId,
            Enum.GetValues<AppointmentStatus>().ToDictionary(status => status, status => status == AppointmentStatus.Pending ? rows : 0),
            new PagedResult<AppointmentReportRow>(
                Enumerable.Range(1, rows)
                    .Select(day => new AppointmentReportRow(
                        new DateOnly(2027, 1, day), new TimeOnly(9, 0), "Ana Pérez", "Cardiología", AppointmentStatus.Pending, null))
                    .ToList(),
                1, rows, rows));
}
