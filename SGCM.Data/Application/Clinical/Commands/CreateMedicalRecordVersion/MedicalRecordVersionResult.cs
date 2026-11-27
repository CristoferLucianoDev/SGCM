namespace SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;

/// <summary>Versión del registro clínico recién creada, que pasa a ser la vigente de la cita.</summary>
/// <param name="Id">Id de la versión.</param>
/// <param name="AppointmentId">Cita registrada.</param>
/// <param name="Diagnosis">Diagnóstico recortado, o <c>null</c> si no se escribió.</param>
/// <param name="Treatment">Tratamiento recortado, o <c>null</c> si no se escribió.</param>
/// <param name="Notes">Notas recortadas, o <c>null</c> si no se escribieron.</param>
/// <param name="CreatedAt">Momento en que se creó, en UTC.</param>
/// <param name="IsCorrection"><c>true</c> si corrige una versión anterior; <c>false</c> si es la primera.</param>
public sealed record MedicalRecordVersionResult(
    int Id,
    int AppointmentId,
    string? Diagnosis,
    string? Treatment,
    string? Notes,
    DateTimeOffset CreatedAt,
    bool IsCorrection);
