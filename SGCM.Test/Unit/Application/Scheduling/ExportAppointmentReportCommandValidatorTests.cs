using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Data.Application.Scheduling.Commands.ExportAppointmentReport;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class ExportAppointmentReportCommandValidatorTests
{
    private const int UndefinedFormat = 99;

    private readonly ExportAppointmentReportCommandValidator _validator = new();

    [Theory]
    [InlineData(ReportFormat.Pdf)]
    [InlineData(ReportFormat.Xlsx)]
    [InlineData(ReportFormat.Docx)]
    public void Cada_formato_definido_es_valido(ReportFormat format)
    {
        _validator.Validate(new ExportAppointmentReportCommand(ReportCriteriaMother.Valid(), format)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_formato_no_definido_se_rechaza()
    {
        AssertSingleError(
            new ExportAppointmentReportCommand(ReportCriteriaMother.Valid(), (ReportFormat)UndefinedFormat), "Report.InvalidFormat");
    }

    [Fact]
    public void Aplica_las_reglas_compartidas_de_los_criterios()
    {
        var criteria = ReportCriteriaMother.Valid() with { From = default };

        AssertSingleError(new ExportAppointmentReportCommand(criteria, ReportFormat.Pdf), "Report.DateRangeRequired");
    }

    [Fact]
    public void Sin_criterios_se_rechaza()
    {
        AssertSingleError(new ExportAppointmentReportCommand(null!, ReportFormat.Pdf), "Report.CriteriaRequired");
    }

    private void AssertSingleError(ExportAppointmentReportCommand command, string code)
    {
        var result = _validator.Validate(command);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
