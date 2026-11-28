using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;

/// <summary>Historial médico de un paciente: sus citas atendidas o con registro clínico, de la más reciente a la más antigua.</summary>
/// <param name="PatientId">Paciente del historial.</param>
/// <param name="Entries">Una entrada por cita.</param>
public sealed record MedicalHistoryDto(int PatientId, IReadOnlyList<MedicalHistoryEntryDto> Entries);

/// <summary>Una cita del historial médico con su registro clínico.</summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="StartsAt">Inicio de la cita en UTC.</param>
/// <param name="EndsAt">Fin de la cita en UTC.</param>
/// <param name="Status">Estado de la cita.</param>
/// <param name="DoctorId">Médico de la cita.</param>
/// <param name="DoctorName">Nombre y apellido del médico.</param>
/// <param name="SpecialtyName">Especialidad de la cita.</param>
/// <param name="CurrentVersion">Versión vigente del registro, o <c>null</c> si la cita no tiene registro clínico.</param>
/// <param name="PreviousVersions">
/// Versiones anteriores, de la más reciente a la más antigua. Solo las ve el médico que atendió la cita; para los
/// demás va vacía.
/// </param>
public sealed record MedicalHistoryEntryDto(
    int AppointmentId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentStatus Status,
    int DoctorId,
    string DoctorName,
    string SpecialtyName,
    MedicalRecordVersionDto? CurrentVersion,
    IReadOnlyList<MedicalRecordVersionDto> PreviousVersions)
{
    /// <summary>Indica si la cita tiene registro clínico.</summary>
    public bool HasMedicalRecord => CurrentVersion is not null;
}

/// <summary>Una versión del registro clínico de una cita.</summary>
/// <param name="Id">Id de la versión.</param>
/// <param name="Diagnosis">Diagnóstico, o <c>null</c> si no se escribió.</param>
/// <param name="Treatment">Tratamiento, o <c>null</c> si no se escribió.</param>
/// <param name="Notes">Notas, o <c>null</c> si no se escribieron.</param>
/// <param name="CreatedAt">Momento en que se creó, en UTC.</param>
public sealed record MedicalRecordVersionDto(
    int Id,
    string? Diagnosis,
    string? Treatment,
    string? Notes,
    DateTimeOffset CreatedAt);
