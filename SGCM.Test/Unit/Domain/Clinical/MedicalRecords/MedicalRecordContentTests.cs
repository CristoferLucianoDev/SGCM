using SGCM.Data.Domain.Clinical.MedicalRecords;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Clinical.MedicalRecords;

/// <summary>
/// Pruebas de <see cref="MedicalRecordContent"/>: diagnóstico, tratamiento y notas opcionales, con al menos uno
/// escrito y dentro de las longitudes del modelo.
/// </summary>
public sealed class MedicalRecordContentTests
{
    [Fact]
    public void Se_crea_con_las_tres_partes_recortadas()
    {
        var content = MedicalRecordContent.Create("  Faringitis  ", " Reposo ", " Control en 7 días ").Value;

        content.Diagnosis.ShouldBe("Faringitis");
        content.Treatment.ShouldBe("Reposo");
        content.Notes.ShouldBe("Control en 7 días");
    }

    [Fact]
    public void Basta_con_una_sola_parte_escrita()
    {
        var content = MedicalRecordContent.Create(diagnosis: null, treatment: null, notes: "Paciente estable").Value;

        content.Diagnosis.ShouldBeNull();
        content.Treatment.ShouldBeNull();
        content.Notes.ShouldBe("Paciente estable");
    }

    [Fact]
    public void Una_parte_en_blanco_cuenta_como_no_escrita()
    {
        var content = MedicalRecordContent.Create("Faringitis", "   ", "").Value;

        content.Treatment.ShouldBeNull();
        content.Notes.ShouldBeNull();
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", " ", "\t")]
    public void Sin_ninguna_parte_escrita_es_un_registro_vacio(string? diagnosis, string? treatment, string? notes)
    {
        MedicalRecordContent.Create(diagnosis, treatment, notes).Error.ShouldBe(MedicalRecordErrors.Empty);
    }

    [Fact]
    public void Se_aceptan_las_longitudes_maximas_del_modelo()
    {
        var result = MedicalRecordContent.Create(
            Text(MedicalRecordContent.DiagnosisMaxLength),
            Text(MedicalRecordContent.TreatmentMaxLength),
            Text(MedicalRecordContent.NotesMaxLength));

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public void Un_diagnostico_mas_largo_que_el_modelo_se_rechaza()
    {
        MedicalRecordContent.Create(Text(MedicalRecordContent.DiagnosisMaxLength + 1), null, null)
            .Error.ShouldBe(MedicalRecordErrors.DiagnosisTooLong);
    }

    [Fact]
    public void Un_tratamiento_mas_largo_que_el_modelo_se_rechaza()
    {
        MedicalRecordContent.Create(null, Text(MedicalRecordContent.TreatmentMaxLength + 1), null)
            .Error.ShouldBe(MedicalRecordErrors.TreatmentTooLong);
    }

    [Fact]
    public void Unas_notas_mas_largas_que_el_modelo_se_rechazan()
    {
        MedicalRecordContent.Create(null, null, Text(MedicalRecordContent.NotesMaxLength + 1))
            .Error.ShouldBe(MedicalRecordErrors.NotesTooLong);
    }

    [Fact]
    public void La_longitud_se_mide_despues_de_recortar()
    {
        var padded = $"  {Text(MedicalRecordContent.DiagnosisMaxLength)}  ";

        MedicalRecordContent.Create(padded, null, null).IsSuccess.ShouldBeTrue();
    }

    private static string Text(int length) => new('a', length);
}
