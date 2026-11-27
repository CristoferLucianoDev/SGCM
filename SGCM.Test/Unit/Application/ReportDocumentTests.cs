using SGCM.Data.Application.Abstractions.Reporting;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application;

public sealed class ReportDocumentTests
{
    [Fact]
    public void El_rango_se_muestra_con_las_dos_fechas_en_formato_local()
    {
        ReportDocumentMother.WithRows(1).Period.ShouldBe("Del 01/01/2027 al 31/01/2027");
    }

    [Fact]
    public void La_hora_de_generacion_se_muestra_en_formato_local()
    {
        ReportDocumentMother.WithRows(1).GeneratedAtText.ShouldBe("Generado el 12/01/2027 09:30");
    }

    [Theory]
    [InlineData("pdf")]
    [InlineData("xlsx")]
    [InlineData("docx")]
    public void El_nombre_del_archivo_lleva_la_fecha_y_hora_de_generacion_y_la_extension(string extension)
    {
        ReportDocumentMother.WithRows(1).FileName(extension).ShouldBe($"reporte-citas-20270112-0930.{extension}");
    }
}
