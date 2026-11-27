using SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetAppointmentReportQueryValidatorTests
{
    private const int LargePageSize = 100;

    private readonly GetAppointmentReportQueryValidator _validator = new();

    [Fact]
    public void Una_consulta_con_criterios_validos_y_la_paginacion_por_defecto_es_valida()
    {
        var query = new GetAppointmentReportQuery(ReportCriteriaMother.Valid());

        _validator.Validate(query).IsValid.ShouldBeTrue();
        query.PageNumber.ShouldBe(GetAppointmentReportQuery.FirstPage);
        query.PageSize.ShouldBe(GetAppointmentReportQuery.DefaultPageSize);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Una_pagina_menor_que_1_se_rechaza(int pageNumber)
    {
        AssertSingleError(new GetAppointmentReportQuery(ReportCriteriaMother.Valid(), pageNumber), "Report.InvalidPageNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GetAppointmentReportQuery.MaximumPageSize + 1)]
    public void Un_tamano_de_pagina_fuera_del_rango_se_rechaza(int pageSize)
    {
        AssertSingleError(new GetAppointmentReportQuery(ReportCriteriaMother.Valid(), 1, pageSize), "Report.InvalidPageSize");
    }

    [Fact]
    public void Un_desplazamiento_que_no_cabe_en_un_entero_se_rechaza()
    {
        AssertSingleError(
            new GetAppointmentReportQuery(ReportCriteriaMother.Valid(), int.MaxValue, LargePageSize), "Report.PageOffsetTooLarge");
    }

    [Fact]
    public void Aplica_las_reglas_compartidas_de_los_criterios()
    {
        var criteria = ReportCriteriaMother.Valid() with { SpecialtyId = 0 };

        AssertSingleError(new GetAppointmentReportQuery(criteria), "Report.InvalidSpecialtyId");
    }

    [Fact]
    public void Sin_criterios_se_rechaza()
    {
        AssertSingleError(new GetAppointmentReportQuery(null!), "Report.CriteriaRequired");
    }

    private void AssertSingleError(GetAppointmentReportQuery query, string code)
    {
        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
