namespace SGCM.Web.Contracts.Clinical;

/// <summary>Versión del registro clínico recién creada; pasa a ser la vigente de la cita.</summary>
/// <param name="Id">Id de la versión.</param>
/// <param name="AppointmentId">Cita registrada.</param>
/// <param name="Diagnosis">Diagnóstico, o <c>null</c> si no se escribió.</param>
/// <param name="Treatment">Tratamiento, o <c>null</c> si no se escribió.</param>
/// <param name="Notes">Notas, o <c>null</c> si no se escribieron.</param>
/// <param name="CreatedAt">Momento de creación, en UTC.</param>
/// <param name="IsCorrection"><c>true</c> si corrige una versión anterior, que se conserva sin cambios.</param>
public sealed record MedicalRecordVersionResponse(
    int Id,
    int AppointmentId,
    string? Diagnosis,
    string? Treatment,
    string? Notes,
    DateTimeOffset CreatedAt,
    bool IsCorrection);
