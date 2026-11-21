using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.Scheduling.Availability;

/// <summary>
/// Errores de la consulta de horarios libres de un médico.
/// </summary>
public static class AvailableSlotsErrors
{
    /// <summary>El paciente no indicó de qué médico quiere ver los horarios.</summary>
    public static readonly Error DoctorRequired = Error.Validation(
        "AvailableSlots.DoctorRequired", "Debe indicar el médico.");

    /// <summary>Solo los pacientes y los médicos consultan horarios libres.</summary>
    public static readonly Error RoleNotAllowed = Error.Forbidden(
        "AvailableSlots.RoleNotAllowed", "Solo los pacientes y los médicos pueden consultar horarios libres.");

    /// <summary>Un médico solo consulta sus propios horarios.</summary>
    public static readonly Error OtherDoctorSchedule = Error.Forbidden(
        "AvailableSlots.OtherDoctorSchedule", "Un médico solo puede consultar sus propios horarios.");
}
