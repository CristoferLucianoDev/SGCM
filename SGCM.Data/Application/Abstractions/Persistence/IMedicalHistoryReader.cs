using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>Lee el historial médico de un paciente sin cargar los agregados.</summary>
public interface IMedicalHistoryReader
{
    /// <summary>
    /// Lista las citas del paciente que entran en su historial médico: las completadas, tengan o no registro clínico,
    /// y cualquier otra que tenga registro. Van de la más reciente a la más antigua por inicio y, a igual inicio, por
    /// id descendente.
    /// </summary>
    /// <param name="patientId">Id del perfil del paciente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>Las citas con todas las versiones de su registro, la vigente primero.</returns>
    Task<IReadOnlyList<MedicalHistoryAppointmentData>> ListAsync(int patientId, CancellationToken cancellationToken);
}

/// <summary>Una cita del historial médico con todas las versiones de su registro clínico.</summary>
/// <param name="AppointmentId">Id de la cita.</param>
/// <param name="StartsAt">Inicio en UTC.</param>
/// <param name="EndsAt">Fin en UTC.</param>
/// <param name="Status">Estado de la cita.</param>
/// <param name="DoctorId">Médico de la cita.</param>
/// <param name="DoctorName">Nombre y apellido del médico.</param>
/// <param name="SpecialtyName">Especialidad de la cita.</param>
/// <param name="Versions">
/// Versiones del registro, de la más reciente a la más antigua: la vigente es la primera. Vacía si la cita no tiene
/// registro.
/// </param>
public sealed record MedicalHistoryAppointmentData(
    int AppointmentId,
    DateTimeOffset StartsAt,
    DateTimeOffset EndsAt,
    AppointmentStatus Status,
    int DoctorId,
    string DoctorName,
    string SpecialtyName,
    IReadOnlyList<MedicalHistoryVersionData> Versions);

/// <summary>Una versión del registro clínico leída para el historial.</summary>
/// <param name="Id">Id de la versión.</param>
/// <param name="Diagnosis">Diagnóstico.</param>
/// <param name="Treatment">Tratamiento.</param>
/// <param name="Notes">Notas.</param>
/// <param name="CreatedAt">Momento de creación en UTC.</param>
public sealed record MedicalHistoryVersionData(
    int Id,
    string? Diagnosis,
    string? Treatment,
    string? Notes,
    DateTimeOffset CreatedAt);
