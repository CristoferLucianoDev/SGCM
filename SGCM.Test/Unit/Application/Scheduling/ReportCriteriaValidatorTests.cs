using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class ReportCriteriaValidatorTests
{
    private const int UndefinedEnumValue = 99;

    private readonly ReportCriteriaValidator _validator = new();

    [Fact]
    public void Unos_criterios_sin_filtros_son_validos()
    {
        _validator.Validate(ReportCriteriaMother.Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Unos_criterios_con_todos_los_filtros_y_orden_son_validos()
    {
        var criteria = ReportCriteriaMother.Valid() with
        {
            Type = ReportType.Pending,
            Status = AppointmentStatus.Confirmed,
            PatientName = "Ana Pérez",
            SpecialtyId = 3,
            SortBy = ReportSortField.Patient,
            SortDirection = ReportSortDirection.Descending
        };

        _validator.Validate(criteria).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_rango_de_un_solo_dia_es_valido()
    {
        var criteria = ReportCriteriaMother.Valid() with { To = ReportCriteriaMother.From };

        _validator.Validate(criteria).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Una_fecha_inicial_posterior_a_la_final_se_rechaza()
    {
        var criteria = ReportCriteriaMother.Valid() with { From = ReportCriteriaMother.To.AddDays(1) };

        AssertSingleError(criteria, "Report.InvalidDateRange");
    }

    [Fact]
    public void Sin_fecha_inicial_se_rechaza()
    {
        AssertSingleError(ReportCriteriaMother.Valid() with { From = default }, "Report.DateRangeRequired");
    }

    [Fact]
    public void Sin_fecha_final_se_rechaza()
    {
        AssertSingleError(ReportCriteriaMother.Valid() with { To = default }, "Report.DateRangeRequired");
    }

    [Fact]
    public void Un_tipo_no_definido_se_rechaza()
    {
        AssertSingleError(ReportCriteriaMother.Valid() with { Type = (ReportType)UndefinedEnumValue }, "Report.InvalidType");
    }

    [Fact]
    public void Un_estado_no_definido_se_rechaza()
    {
        var criteria = ReportCriteriaMother.Valid() with { Status = (AppointmentStatus)UndefinedEnumValue };

        AssertSingleError(criteria, "Report.InvalidStatus");
    }

    [Fact]
    public void Una_columna_de_orden_no_definida_se_rechaza()
    {
        var criteria = ReportCriteriaMother.Valid() with { SortBy = (ReportSortField)UndefinedEnumValue };

        AssertSingleError(criteria, "Report.InvalidSortField");
    }

    [Fact]
    public void Un_sentido_de_orden_no_definido_se_rechaza()
    {
        var criteria = ReportCriteriaMother.Valid() with { SortDirection = (ReportSortDirection)UndefinedEnumValue };

        AssertSingleError(criteria, "Report.InvalidSortDirection");
    }

    [Fact]
    public void Un_nombre_de_paciente_de_la_longitud_maxima_es_valido()
    {
        var criteria = ReportCriteriaMother.Valid() with { PatientName = new string('a', ReportCriteria.PatientNameMaxLength) };

        _validator.Validate(criteria).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_nombre_de_paciente_demasiado_largo_se_rechaza()
    {
        var criteria = ReportCriteriaMother.Valid() with { PatientName = new string('a', ReportCriteria.PatientNameMaxLength + 1) };

        AssertSingleError(criteria, "Report.PatientNameTooLong");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void La_especialidad_debe_ser_positiva(int specialtyId)
    {
        AssertSingleError(ReportCriteriaMother.Valid() with { SpecialtyId = specialtyId }, "Report.InvalidSpecialtyId");
    }

    private void AssertSingleError(ReportCriteria criteria, string code)
    {
        var result = _validator.Validate(criteria);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
