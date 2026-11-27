using SGCM.Data.Domain.Common.Exceptions;

namespace SGCM.Data.Domain.Clinical.MedicalHistory;

/// <summary>
/// Acceso concedido al historial médico de un paciente: quién lo consulta y qué puede ver. El paciente ve la versión
/// vigente del registro de cada una de sus citas; el médico tratante ve además las versiones anteriores, pero solo
/// en las citas que él atendió.
/// </summary>
public sealed record MedicalHistoryAccess
{
    private MedicalHistoryAccess(int patientId, int? doctorId)
    {
        PatientId = patientId;
        DoctorId = doctorId;
    }

    /// <summary>Paciente cuyo historial se consulta.</summary>
    public int PatientId { get; }

    /// <summary>Médico que consulta, o <c>null</c> si consulta el propio paciente.</summary>
    public int? DoctorId { get; }

    /// <summary>Indica si quien consulta es un médico.</summary>
    public bool IsDoctor => DoctorId is not null;

    /// <summary>El paciente consulta su propio historial.</summary>
    /// <param name="patientId">Perfil del paciente autenticado.</param>
    /// <exception cref="DomainException">Si el paciente no es válido.</exception>
    public static MedicalHistoryAccess ForPatient(int patientId)
    {
        EnsureValid(patientId, "paciente");
        return new MedicalHistoryAccess(patientId, doctorId: null);
    }

    /// <summary>
    /// Un médico consulta el historial de un paciente con quien tiene al menos una cita; esa relación la comprueba
    /// quien concede el acceso.
    /// </summary>
    /// <param name="doctorId">Perfil del médico autenticado.</param>
    /// <param name="patientId">Paciente cuyo historial consulta.</param>
    /// <exception cref="DomainException">Si el médico o el paciente no son válidos.</exception>
    public static MedicalHistoryAccess ForTreatingDoctor(int doctorId, int patientId)
    {
        EnsureValid(doctorId, "médico");
        EnsureValid(patientId, "paciente");
        return new MedicalHistoryAccess(patientId, doctorId);
    }

    /// <summary>
    /// Indica si quien consulta ve las versiones anteriores del registro de una cita: solo el médico que la atendió.
    /// </summary>
    /// <param name="appointmentDoctorId">Médico de la cita.</param>
    public bool ShowsPreviousVersions(int appointmentDoctorId) => DoctorId == appointmentDoctorId;

    // Los ids llegan de quien concede el acceso, no del usuario: uno inválido es un error de programación.
    private static void EnsureValid(int id, string who)
    {
        if (id <= 0) throw new DomainException($"El {who} del acceso al historial médico no es válido.");
    }
}
