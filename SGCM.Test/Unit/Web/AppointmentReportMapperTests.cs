using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AppointmentReportMapperTests
{
    private const string EmptyReportMessage = "No hay citas del tipo y el rango de fechas elegidos.";
    private const int UndefinedValue = 99;

    private static readonly DateOnly From = new(2027, 1, 1);
    private static readonly DateOnly To = new(2027, 1, 31);

    [Fact]
    public void La_solicitud_completa_se_convierte_en_el_comando_con_todos_los_criterios()
    {
        var request = new GenerateAppointmentReportRequest(
            AppointmentReportType.Pending, From, To, AppointmentStatusFilter.Confirmed, "Ana", 3,
            AppointmentReportSortField.Patient, AppointmentReportSortDirection.Descending, 50);

        request.ToCommand().ShouldBe(new GenerateAppointmentReportCommand(
            new ReportCriteria(ReportType.Pending, From, To, AppointmentStatus.Confirmed, "Ana", 3,
                ReportSortField.Patient, ReportSortDirection.Descending),
            50));
    }

    [Theory]
    [InlineData(AppointmentReportType.Completed, ReportType.Completed)]
    [InlineData(AppointmentReportType.Cancelled, ReportType.Cancelled)]
    [InlineData(AppointmentReportType.Pending, ReportType.Pending)]
    [InlineData(AppointmentReportType.All, ReportType.All)]
    public void Cada_tipo_pasa_al_tipo_del_dominio(AppointmentReportType type, ReportType expected)
    {
        new GenerateAppointmentReportRequest(type, From, To).ToCommand().Criteria.Type.ShouldBe(expected);
    }

    [Fact]
    public void Sin_filtros_usa_el_orden_y_la_pagina_por_defecto()
    {
        var command = new GenerateAppointmentReportRequest(AppointmentReportType.All, From, To).ToCommand();

        command.ShouldBe(new GenerateAppointmentReportCommand(new ReportCriteria(ReportType.All, From, To)));
        command.PageSize.ShouldBe(GenerateAppointmentReportCommand.DefaultPageSize);
    }

    [Fact]
    public void Las_fechas_ausentes_pasan_por_defecto_para_que_las_rechace_el_validador()
    {
        var criteria = new GenerateAppointmentReportRequest(AppointmentReportType.All, null, null).ToCommand().Criteria;

        criteria.From.ShouldBe(default);
        criteria.To.ShouldBe(default);
    }

    [Fact]
    public void Un_valor_no_definido_pasa_tal_cual_para_que_lo_rechace_el_validador()
    {
        var criteria = new GenerateAppointmentReportRequest(
            (AppointmentReportType)UndefinedValue, From, To, (AppointmentStatusFilter)UndefinedValue,
            SortBy: (AppointmentReportSortField)UndefinedValue,
            SortDirection: (AppointmentReportSortDirection)UndefinedValue).ToCommand().Criteria;

        criteria.Type.ShouldBe((ReportType)UndefinedValue);
        criteria.Status.ShouldBe((AppointmentStatus)UndefinedValue);
        criteria.SortBy.ShouldBe((ReportSortField)UndefinedValue);
        criteria.SortDirection.ShouldBe((ReportSortDirection)UndefinedValue);
    }

    [Fact]
    public void Una_solicitud_sin_tipo_o_nula_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentException>(() => new GenerateAppointmentReportRequest(null, From, To).ToCommand());
        Should.Throw<ArgumentNullException>(() => ((GenerateAppointmentReportRequest)null!).ToCommand());
    }

    [Fact]
    public void El_reporte_se_copia_con_los_cinco_totales_en_orden_y_las_filas_sin_identificadores()
    {
        var row = new AppointmentReportRow(
            new DateOnly(2027, 1, 12), new TimeOnly(9, 0), "Ana Pérez", "Cardiología", AppointmentStatus.Confirmed, "Control");
        var report = new AppointmentReport(
            7,
            new Dictionary<AppointmentStatus, int> { [AppointmentStatus.Confirmed] = 1, [AppointmentStatus.Completed] = 4 },
            new PagedResult<AppointmentReportRow>([row], 1, 20, 5));

        var response = report.ToResponse();

        response.Totals.ShouldBe(new[]
        {
            new AppointmentReportStatusTotalResponse("pending", 0),
            new AppointmentReportStatusTotalResponse("confirmed", 1),
            new AppointmentReportStatusTotalResponse("rejected", 0),
            new AppointmentReportStatusTotalResponse("cancelled", 0),
            new AppointmentReportStatusTotalResponse("completed", 4)
        });
        response.Items.ShouldBe(new[]
        {
            new AppointmentReportRowResponse(new DateOnly(2027, 1, 12), new TimeOnly(9, 0), "Ana Pérez", "Cardiología", "confirmed", "Control")
        });
        response.PageNumber.ShouldBe(1);
        response.PageSize.ShouldBe(20);
        response.TotalCount.ShouldBe(5);
        response.Message.ShouldBeNull();
        typeof(AppointmentReportResponse).GetProperties().Select(property => property.Name).ShouldNotContain("DoctorId");
        typeof(AppointmentReportRowResponse).GetProperties().Select(property => property.Name)
            .ShouldAllBe(name => !name.EndsWith("Id", StringComparison.Ordinal));
    }

    [Fact]
    public void Un_reporte_sin_filas_avisa_con_un_mensaje()
    {
        var report = new AppointmentReport(
            7, new Dictionary<AppointmentStatus, int>(), new PagedResult<AppointmentReportRow>([], 1, 20, 0));

        var response = report.ToResponse();

        response.Items.ShouldBeEmpty();
        response.Totals.ShouldAllBe(total => total.Count == 0);
        response.Message.ShouldBe(EmptyReportMessage);
    }

    [Fact]
    public void Los_parametros_del_reporte_filtrado_se_convierten_en_la_consulta_con_todos_los_criterios()
    {
        var request = new GetAppointmentReportRequest(
            AppointmentReportType.Cancelled, From, To, AppointmentStatusFilter.Rejected, "Ana", 3,
            AppointmentReportSortField.Status, AppointmentReportSortDirection.Descending, 4, 15);

        request.ToQuery().ShouldBe(new GetAppointmentReportQuery(
            new ReportCriteria(ReportType.Cancelled, From, To, AppointmentStatus.Rejected, "Ana", 3,
                ReportSortField.Status, ReportSortDirection.Descending),
            4, 15));
    }

    [Fact]
    public void El_reporte_filtrado_sin_parametros_opcionales_usa_la_primera_pagina_y_el_orden_por_defecto()
    {
        new GetAppointmentReportRequest(AppointmentReportType.All, From, To).ToQuery()
            .ShouldBe(new GetAppointmentReportQuery(new ReportCriteria(ReportType.All, From, To)));
    }

    [Fact]
    public void Filtrar_y_generar_producen_los_mismos_criterios_con_los_mismos_parametros()
    {
        var generated = new GenerateAppointmentReportRequest(
            AppointmentReportType.Pending, From, To, AppointmentStatusFilter.Pending, "Ana", 3,
            AppointmentReportSortField.Patient, AppointmentReportSortDirection.Descending).ToCommand().Criteria;
        var filtered = new GetAppointmentReportRequest(
            AppointmentReportType.Pending, From, To, AppointmentStatusFilter.Pending, "Ana", 3,
            AppointmentReportSortField.Patient, AppointmentReportSortDirection.Descending).ToQuery().Criteria;

        filtered.ShouldBe(generated);
    }

    [Fact]
    public void El_reporte_filtrado_sin_tipo_o_nulo_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentException>(() => new GetAppointmentReportRequest(null, From, To).ToQuery());
        Should.Throw<ArgumentNullException>(() => ((GetAppointmentReportRequest)null!).ToQuery());
    }

    [Theory]
    [InlineData(AppointmentReportFormat.Pdf, ReportFormat.Pdf)]
    [InlineData(AppointmentReportFormat.Xlsx, ReportFormat.Xlsx)]
    [InlineData(AppointmentReportFormat.Docx, ReportFormat.Docx)]
    public void La_solicitud_de_exportacion_se_convierte_en_el_comando_con_el_formato_y_los_criterios(
        AppointmentReportFormat format, ReportFormat expected)
    {
        var request = new ExportAppointmentReportRequest(
            format, AppointmentReportType.Pending, From, To, AppointmentStatusFilter.Confirmed, "Ana", 3,
            AppointmentReportSortField.Patient, AppointmentReportSortDirection.Descending);

        request.ToCommand().ShouldBe(new ExportAppointmentReportCommand(
            new ReportCriteria(ReportType.Pending, From, To, AppointmentStatus.Confirmed, "Ana", 3,
                ReportSortField.Patient, ReportSortDirection.Descending),
            expected));
    }

    [Fact]
    public void Exportar_y_filtrar_producen_los_mismos_criterios_con_los_mismos_parametros()
    {
        var exported = new ExportAppointmentReportRequest(
            AppointmentReportFormat.Pdf, AppointmentReportType.All, From, To, SortBy: AppointmentReportSortField.Status)
            .ToCommand().Criteria;
        var filtered = new GetAppointmentReportRequest(
            AppointmentReportType.All, From, To, SortBy: AppointmentReportSortField.Status).ToQuery().Criteria;

        exported.ShouldBe(filtered);
    }

    [Fact]
    public void Un_formato_no_definido_pasa_tal_cual_para_que_lo_rechace_el_validador()
    {
        new ExportAppointmentReportRequest((AppointmentReportFormat)UndefinedValue, AppointmentReportType.All, From, To)
            .ToCommand().Format.ShouldBe((ReportFormat)UndefinedValue);
    }

    [Fact]
    public void Una_solicitud_de_exportacion_sin_formato_sin_tipo_o_nula_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentException>(() =>
            new ExportAppointmentReportRequest(null, AppointmentReportType.All, From, To).ToCommand());
        Should.Throw<ArgumentException>(() =>
            new ExportAppointmentReportRequest(AppointmentReportFormat.Pdf, null, From, To).ToCommand());
        Should.Throw<ArgumentNullException>(() => ((ExportAppointmentReportRequest)null!).ToCommand());
    }
}
