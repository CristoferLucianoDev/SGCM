using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Clinical.MedicalHistory;

/// <summary>Errores de la consulta del historial médico.</summary>
public static class MedicalHistoryErrors
{
    /// <summary>El historial propio solo lo consulta un paciente.</summary>
    public static readonly Error PatientOnly = Error.Forbidden(
        "MedicalHistory.PatientOnly", "Solo un paciente puede consultar su propio historial médico.");

    /// <summary>El historial de un paciente solo lo consulta un médico.</summary>
    public static readonly Error DoctorOnly = Error.Forbidden(
        "MedicalHistory.DoctorOnly", "Solo un médico puede consultar el historial médico de un paciente.");

    /// <summary>
    /// Un médico sin ninguna cita con el paciente no consulta su historial. Un paciente inexistente responde igual,
    /// para no revelar qué pacientes existen.
    /// </summary>
    public static readonly Error NoAppointmentWithPatient = Error.Forbidden(
        "MedicalHistory.NoAppointmentWithPatient",
        "Solo puede consultar el historial médico de un paciente con quien tiene al menos una cita.");
}
