using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Reports;

public sealed class AppointmentReportLogTests
{
    private const int DoctorId = 7;
    private const int RowCount = 12;

    private static readonly DateTimeOffset GeneratedAt = new(2027, 1, 12, 9, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void La_generacion_guarda_medico_tipo_filtros_filas_y_hora_en_UTC_sin_formato()
    {
        var criteria = ReportCriteriaMother.Valid() with { Type = ReportType.Cancelled, PatientName = "Ana" };

        var log = AppointmentReportLog.ForGeneration(DoctorId, criteria, RowCount, GeneratedAt);

        log.DoctorId.ShouldBe(DoctorId);
        log.Action.ShouldBe(ReportAction.Generate);
        log.ReportType.ShouldBe(ReportType.Cancelled);
        log.Filters.ShouldBe(criteria.ToJson());
        log.ExportFormat.ShouldBeNull();
        log.RowCount.ShouldBe(RowCount);
        log.GeneratedAt.ShouldBe(GeneratedAt);
        log.GeneratedAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("pdf", "pdf")]
    [InlineData("XLSX", "xlsx")]
    [InlineData(" docx ", "docx")]
    public void La_exportacion_guarda_el_formato_normalizado(string format, string expected)
    {
        var log = AppointmentReportLog.ForExport(DoctorId, ReportCriteriaMother.Valid(), format, RowCount, GeneratedAt);

        log.Action.ShouldBe(ReportAction.Export);
        log.ExportFormat.ShouldBe(expected);
        log.RowCount.ShouldBe(RowCount);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("")]
    [InlineData("   ")]
    public void Un_formato_de_exportacion_no_aceptado_es_un_error_de_programacion(string format)
    {
        Should.Throw<DomainException>(() =>
            AppointmentReportLog.ForExport(DoctorId, ReportCriteriaMother.Valid(), format, RowCount, GeneratedAt));
    }

    [Fact]
    public void Un_reporte_sin_filas_se_registra_igual()
    {
        var log = AppointmentReportLog.ForGeneration(DoctorId, ReportCriteriaMother.Valid(), 0, GeneratedAt);

        log.RowCount.ShouldBe(0);
    }

    [Fact]
    public void Una_cantidad_de_filas_negativa_es_un_error_de_programacion()
    {
        Should.Throw<DomainException>(() =>
            AppointmentReportLog.ForGeneration(DoctorId, ReportCriteriaMother.Valid(), -1, GeneratedAt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_medico_no_valido_es_un_error_de_programacion(int doctorId)
    {
        Should.Throw<DomainException>(() =>
            AppointmentReportLog.ForGeneration(doctorId, ReportCriteriaMother.Valid(), RowCount, GeneratedAt));
    }

    [Fact]
    public void Sin_criterios_lanza_una_excepcion()
    {
        Should.Throw<ArgumentNullException>(() =>
            AppointmentReportLog.ForGeneration(DoctorId, null!, RowCount, GeneratedAt));
    }

    [Fact]
    public void No_genera_eventos_de_dominio()
    {
        var log = AppointmentReportLog.ForGeneration(DoctorId, ReportCriteriaMother.Valid(), RowCount, GeneratedAt);

        log.DomainEvents.ShouldBeEmpty();
    }
}
