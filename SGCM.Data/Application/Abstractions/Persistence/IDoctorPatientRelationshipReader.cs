namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>Comprueba si un médico y un paciente tienen relación de atención.</summary>
public interface IDoctorPatientRelationshipReader
{
    /// <summary>Indica si el médico tiene al menos una cita con el paciente, en cualquier estado.</summary>
    /// <param name="doctorId">Perfil del médico.</param>
    /// <param name="patientId">Perfil del paciente.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns><c>true</c> si existe al menos una cita entre ambos; <c>false</c> si no, o si alguno no existe.</returns>
    Task<bool> HasAppointmentWithPatientAsync(int doctorId, int patientId, CancellationToken cancellationToken);
}
