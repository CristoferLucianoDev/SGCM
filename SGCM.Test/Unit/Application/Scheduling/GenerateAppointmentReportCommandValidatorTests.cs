using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GenerateAppointmentReportCommandValidatorTests
{
    private readonly GenerateAppointmentReportCommandValidator _validator = new();

    [Fact]
    public void Un_comando_con_criterios_validos_y_la_pagina_por_defecto_es_valido()
    {
        _validator.Validate(new GenerateAppointmentReportCommand(ReportCriteriaMother.Valid())).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(GenerateAppointmentReportCommand.MaximumPageSize)]
    public void Los_extremos_del_tamano_de_pagina_son_validos(int pageSize)
    {
        _validator.Validate(new GenerateAppointmentReportCommand(ReportCriteriaMother.Valid(), pageSize))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(GenerateAppointmentReportCommand.MaximumPageSize + 1)]
    public void Un_tamano_de_pagina_fuera_del_rango_se_rechaza(int pageSize)
    {
        AssertSingleError(new GenerateAppointmentReportCommand(ReportCriteriaMother.Valid(), pageSize), "Report.InvalidPageSize");
    }

    [Fact]
    public void Aplica_las_reglas_compartidas_de_los_criterios()
    {
        var criteria = ReportCriteriaMother.Valid() with { From = ReportCriteriaMother.To.AddDays(1) };

        AssertSingleError(new GenerateAppointmentReportCommand(criteria), "Report.InvalidDateRange");
    }

    [Fact]
    public void Sin_criterios_se_rechaza()
    {
        AssertSingleError(new GenerateAppointmentReportCommand(null!), "Report.CriteriaRequired");
    }

    private void AssertSingleError(GenerateAppointmentReportCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
