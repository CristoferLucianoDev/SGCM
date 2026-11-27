namespace SGCM.Web.Contracts.Clinical;

/// <summary>Información clínica que el médico registra para una cita propia. Al menos una parte debe tener texto.</summary>
/// <param name="Diagnosis">Diagnóstico, hasta 1000 caracteres.</param>
/// <param name="Treatment">Tratamiento, hasta 1000 caracteres.</param>
/// <param name="Notes">Notas, hasta 2000 caracteres.</param>
public sealed record CreateMedicalRecordVersionRequest(
    string? Diagnosis = null,
    string? Treatment = null,
    string? Notes = null);
