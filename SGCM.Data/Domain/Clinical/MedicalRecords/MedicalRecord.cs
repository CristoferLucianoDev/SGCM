using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;

namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>
/// Una versión del registro clínico de una cita. Es de solo inserción: corregir el registro no modifica esta
/// versión, crea una nueva con <see cref="Correct"/>. La vigente de una cita es la más reciente
/// (<see cref="Current"/>), con el mismo criterio que la vista <c>clinical.vw_medical_record_current</c>.
/// </summary>
/// <remarks>
/// Quién puede registrar y cuándo lo decide <c>Appointment.EnsureClinicalRecordAllowed</c>; este agregado solo
/// garantiza el contenido y la historia de versiones.
/// </remarks>
public sealed class MedicalRecord : AggregateRoot
{
    private MedicalRecord()
    {
    }

    /// <summary>Cita a la que pertenece el registro.</summary>
    public int AppointmentId { get; private set; }

    /// <summary>Paciente de la cita.</summary>
    public int PatientId { get; private set; }

    /// <summary>Diagnóstico de esta versión, si se escribió.</summary>
    public string? Diagnosis { get; private set; }

    /// <summary>Tratamiento de esta versión, si se escribió.</summary>
    public string? Treatment { get; private set; }

    /// <summary>Notas de esta versión, si se escribieron.</summary>
    public string? Notes { get; private set; }

    /// <summary>Momento en que se creó esta versión, en UTC.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Usuario que creó esta versión: el del médico de la cita.</summary>
    public int CreatedBy { get; private set; }

    /// <summary>Crea la primera versión del registro clínico de una cita.</summary>
    /// <param name="appointmentId">Cita registrada.</param>
    /// <param name="patientId">Paciente de la cita.</param>
    /// <param name="content">Diagnóstico, tratamiento y notas ya validados.</param>
    /// <param name="createdBy">Usuario del médico que registra.</param>
    /// <param name="createdAt">Momento del registro.</param>
    /// <exception cref="DomainException">Si la cita, el paciente o el usuario no son válidos.</exception>
    public static MedicalRecord Create(
        int appointmentId, int patientId, MedicalRecordContent content, int createdBy, DateTimeOffset createdAt)
    {
        // Los identificadores llegan del caso de uso, no del usuario: uno inválido es un error de programación.
        if (appointmentId <= 0) throw new DomainException("La cita del registro clínico no es válida.");
        if (patientId <= 0) throw new DomainException("El paciente del registro clínico no es válido.");

        return NewVersion(appointmentId, patientId, content, createdBy, createdAt);
    }

    /// <summary>
    /// Corrige el registro creando una versión nueva de la misma cita y el mismo paciente. Esta versión no cambia.
    /// </summary>
    /// <param name="content">Contenido corregido, ya validado.</param>
    /// <param name="createdBy">Usuario del médico que corrige.</param>
    /// <param name="createdAt">Momento de la corrección.</param>
    /// <returns>La versión nueva, que pasa a ser la vigente.</returns>
    /// <exception cref="DomainException">
    /// Si el usuario no es válido o el momento es anterior al de esta versión, porque la nueva no sería la vigente.
    /// </exception>
    public MedicalRecord Correct(MedicalRecordContent content, int createdBy, DateTimeOffset createdAt)
    {
        if (createdAt < CreatedAt)
            throw new DomainException("La corrección del registro clínico no puede ser anterior a la versión corregida.");

        return NewVersion(AppointmentId, PatientId, content, createdBy, createdAt);
    }

    /// <summary>
    /// Devuelve la versión vigente de una cita: la de <see cref="CreatedAt"/> más reciente y, si coinciden, la de
    /// mayor <see cref="Entity.Id"/>.
    /// </summary>
    /// <param name="versions">Versiones del registro de una misma cita.</param>
    /// <returns>La versión vigente, o <c>null</c> si la cita no tiene registro.</returns>
    /// <exception cref="DomainException">Si las versiones son de citas distintas.</exception>
    public static MedicalRecord? Current(IEnumerable<MedicalRecord> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        var list = versions.ToList();
        if (list.Select(version => version.AppointmentId).Distinct().Skip(1).Any())
            throw new DomainException("Las versiones del registro clínico deben ser de una misma cita.");

        return list
            .OrderByDescending(version => version.CreatedAt)
            .ThenByDescending(version => version.Id)
            .FirstOrDefault();
    }

    private static MedicalRecord NewVersion(
        int appointmentId, int patientId, MedicalRecordContent content, int createdBy, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (createdBy <= 0) throw new DomainException("El usuario que registra la información clínica no es válido.");

        return new MedicalRecord
        {
            AppointmentId = appointmentId,
            PatientId = patientId,
            Diagnosis = content.Diagnosis,
            Treatment = content.Treatment,
            Notes = content.Notes,
            CreatedAt = createdAt.ToUniversalTime(),
            CreatedBy = createdBy
        };
    }
}
