using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Reports;

public sealed class ReportCriteriaTests
{
    [Fact]
    public void Sin_filtros_se_serializa_con_el_tipo_el_rango_y_el_orden_por_defecto()
    {
        var json = ReportCriteriaMother.Valid().ToJson();

        json.ShouldBe("""{"type":"all","from":"2027-01-01","to":"2027-01-31","sort_by":"date","sort_direction":"ascending"}""");
    }

    [Fact]
    public void Con_todos_los_filtros_se_serializan_tambien_los_filtros_en_snake_case()
    {
        var criteria = ReportCriteriaMother.Valid() with
        {
            Type = ReportType.Cancelled,
            Status = AppointmentStatus.Cancelled,
            PatientName = "Ana",
            SpecialtyId = 3,
            SortBy = ReportSortField.Patient,
            SortDirection = ReportSortDirection.Descending
        };

        var json = criteria.ToJson();

        json.ShouldBe(
            """{"type":"cancelled","from":"2027-01-01","to":"2027-01-31","status":"cancelled","patient_name":"Ana","specialty_id":3,"sort_by":"patient","sort_direction":"descending"}""");
    }

    [Fact]
    public void Por_defecto_ordena_por_fecha_ascendente()
    {
        var criteria = ReportCriteriaMother.Valid();

        criteria.SortBy.ShouldBe(ReportSortField.Date);
        criteria.SortDirection.ShouldBe(ReportSortDirection.Ascending);
    }
}
