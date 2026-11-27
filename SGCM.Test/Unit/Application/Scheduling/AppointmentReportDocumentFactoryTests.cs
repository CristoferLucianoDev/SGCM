using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class AppointmentReportDocumentFactoryTests
{
    private static readonly DateTimeOffset GeneratedAt = new(2027, 1, 12, 9, 30, 0, TimeSpan.FromHours(-4));

    [Theory]
    [InlineData(ReportType.Completed, "Reporte de citas: realizadas")]
    [InlineData(ReportType.Cancelled, "Reporte de citas: canceladas")]
    [InlineData(ReportType.Pending, "Reporte de citas: pendientes")]
    [InlineData(ReportType.All, "Reporte de citas: todas")]
    public void El_titulo_indica_el_tipo_de_reporte(ReportType type, string title)
    {
        var document = AppointmentReportDocumentFactory.Create(Report(), ReportCriteriaMother.Valid() with { Type = type }, GeneratedAt);

        document.Title.ShouldBe(title);
    }

    [Fact]
    public void Copia_el_rango_de_los_criterios_y_la_hora_de_generacion()
    {
        var document = AppointmentReportDocumentFactory.Create(Report(), ReportCriteriaMother.Valid(), GeneratedAt);

        document.From.ShouldBe(ReportCriteriaMother.From);
        document.To.ShouldBe(ReportCriteriaMother.To);
        document.GeneratedAt.ShouldBe(GeneratedAt);
    }

    [Fact]
    public void Las_filas_tienen_las_columnas_de_la_pantalla_en_texto_y_en_el_mismo_orden()
    {
        var document = AppointmentReportDocumentFactory.Create(Report(), ReportCriteriaMother.Valid(), GeneratedAt);

        document.Columns.ShouldBe(new[] { "Fecha", "Hora", "Paciente", "Especialidad", "Estado", "Motivo de consulta" });
        document.Rows.Count.ShouldBe(2);
        document.Rows[0].ShouldBe(new[] { "12/01/2027", "09:00", "Ana Pérez", "Cardiología", "Confirmada", "Control anual" });
        document.Rows[1].ShouldBe(new[] { "13/01/2027", "14:00", "Bruno Díaz", "Pediatría", "Cancelada", string.Empty });
    }

    [Fact]
    public void Los_totales_usan_el_nombre_de_cada_estado_en_el_orden_del_ciclo_de_vida()
    {
        var document = AppointmentReportDocumentFactory.Create(Report(), ReportCriteriaMother.Valid(), GeneratedAt);

        document.TotalsByStatus.Keys.ShouldBe(new[] { "Pendiente", "Confirmada", "Rechazada", "Cancelada", "Completada" });
        document.TotalsByStatus["Confirmada"].ShouldBe(1);
        document.TotalsByStatus["Cancelada"].ShouldBe(1);
        document.TotalsByStatus["Pendiente"].ShouldBe(0);
    }

    private static AppointmentReport Report() =>
        new(7,
            new Dictionary<AppointmentStatus, int> { [AppointmentStatus.Confirmed] = 1, [AppointmentStatus.Cancelled] = 1 },
            new PagedResult<AppointmentReportRow>(
            [
                new(new DateOnly(2027, 1, 12), new TimeOnly(9, 0), "Ana Pérez", "Cardiología", AppointmentStatus.Confirmed, "Control anual"),
                new(new DateOnly(2027, 1, 13), new TimeOnly(14, 0), "Bruno Díaz", "Pediatría", AppointmentStatus.Cancelled, null)
            ], 1, 2, 2));
}
