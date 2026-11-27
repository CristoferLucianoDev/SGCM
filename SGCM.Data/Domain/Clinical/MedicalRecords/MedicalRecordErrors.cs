using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>Errores del registro clínico de una cita.</summary>
public static class MedicalRecordErrors
{
    /// <summary>Una versión sin diagnóstico, tratamiento ni notas no registra nada.</summary>
    public static readonly Error Empty = Error.Validation(
        "MedicalRecord.Empty", "El registro clínico debe tener al menos un diagnóstico, un tratamiento o notas.");

    /// <summary>El diagnóstico supera la longitud de la columna.</summary>
    public static readonly Error DiagnosisTooLong = Error.Validation(
        "MedicalRecord.DiagnosisTooLong",
        $"El diagnóstico no puede superar los {MedicalRecordContent.DiagnosisMaxLength} caracteres.");

    /// <summary>El tratamiento supera la longitud de la columna.</summary>
    public static readonly Error TreatmentTooLong = Error.Validation(
        "MedicalRecord.TreatmentTooLong",
        $"El tratamiento no puede superar los {MedicalRecordContent.TreatmentMaxLength} caracteres.");

    /// <summary>Las notas superan la longitud de la columna.</summary>
    public static readonly Error NotesTooLong = Error.Validation(
        "MedicalRecord.NotesTooLong",
        $"Las notas no pueden superar los {MedicalRecordContent.NotesMaxLength} caracteres.");
}
