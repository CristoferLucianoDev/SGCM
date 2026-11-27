using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>
/// Lo que el médico escribe en una versión del registro clínico: diagnóstico, tratamiento y notas. Cada parte es
/// opcional, pero al menos una debe tener texto. Las longitudes son las de las columnas de
/// <c>clinical.medical_record</c>.
/// </summary>
public sealed record MedicalRecordContent
{
    /// <summary>Longitud máxima del diagnóstico, igual que en la base.</summary>
    public const int DiagnosisMaxLength = 1000;

    /// <summary>Longitud máxima del tratamiento, igual que en la base.</summary>
    public const int TreatmentMaxLength = 1000;

    /// <summary>Longitud máxima de las notas, igual que en la base.</summary>
    public const int NotesMaxLength = 2000;

    private MedicalRecordContent(string? diagnosis, string? treatment, string? notes)
    {
        Diagnosis = diagnosis;
        Treatment = treatment;
        Notes = notes;
    }

    /// <summary>Diagnóstico recortado, o <c>null</c> si no se escribió.</summary>
    public string? Diagnosis { get; }

    /// <summary>Tratamiento recortado, o <c>null</c> si no se escribió.</summary>
    public string? Treatment { get; }

    /// <summary>Notas recortadas, o <c>null</c> si no se escribieron.</summary>
    public string? Notes { get; }

    /// <summary>Crea el contenido de una versión.</summary>
    /// <param name="diagnosis">Diagnóstico; se recorta, y en blanco cuenta como no escrito.</param>
    /// <param name="treatment">Tratamiento; se recorta, y en blanco cuenta como no escrito.</param>
    /// <param name="notes">Notas; se recortan, y en blanco cuentan como no escritas.</param>
    /// <returns>
    /// El contenido, <see cref="MedicalRecordErrors.Empty"/> si las tres partes están vacías, o el error de la
    /// primera parte que, recortada, supera su longitud máxima (diagnóstico, tratamiento y notas, en ese orden).
    /// </returns>
    public static Result<MedicalRecordContent> Create(string? diagnosis, string? treatment, string? notes)
    {
        var trimmedDiagnosis = NormalizeOptional(diagnosis);
        var trimmedTreatment = NormalizeOptional(treatment);
        var trimmedNotes = NormalizeOptional(notes);

        if (trimmedDiagnosis is null && trimmedTreatment is null && trimmedNotes is null)
            return MedicalRecordErrors.Empty;
        if (trimmedDiagnosis?.Length > DiagnosisMaxLength) return MedicalRecordErrors.DiagnosisTooLong;
        if (trimmedTreatment?.Length > TreatmentMaxLength) return MedicalRecordErrors.TreatmentTooLong;
        if (trimmedNotes?.Length > NotesMaxLength) return MedicalRecordErrors.NotesTooLong;

        return new MedicalRecordContent(trimmedDiagnosis, trimmedTreatment, trimmedNotes);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
