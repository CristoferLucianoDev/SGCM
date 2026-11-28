using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;

namespace SGCM.Data.Application.Clinical;

/// <summary>
/// Decide quién consulta el historial médico de un paciente. El paciente solo consulta el suyo, porque su id sale del
/// usuario autenticado. Un médico consulta el de un paciente con quien tiene al menos una cita, en cualquier estado;
/// si no la tiene, o el paciente no existe, recibe el mismo rechazo para no revelar qué pacientes existen.
/// </summary>
internal sealed class MedicalHistoryAccessPolicy(
    ICurrentUser currentUser,
    IPatientRepository patients,
    IActiveDoctorReader doctors,
    IDoctorPatientRelationshipReader relationships)
{
    /// <summary>Concede al paciente autenticado el acceso a su propio historial.</summary>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// El acceso, <see cref="MedicalHistoryErrors.PatientOnly"/> si no hay un paciente autenticado, o
    /// <see cref="PatientErrors.ProfileNotFound"/> si aún no completó su perfil.
    /// </returns>
    public async Task<Result<MedicalHistoryAccess>> ForCurrentPatientAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Patient))
            return MedicalHistoryErrors.PatientOnly;

        var patient = await patients.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (patient is null) return PatientErrors.ProfileNotFound;

        return MedicalHistoryAccess.ForPatient(patient.Id);
    }

    /// <summary>Concede al médico autenticado el acceso al historial de un paciente.</summary>
    /// <param name="patientId">Paciente cuyo historial se consulta.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// El acceso, <see cref="MedicalHistoryErrors.DoctorOnly"/> si no hay un médico autenticado,
    /// <see cref="DoctorErrors.NotFound"/> si el médico no tiene perfil activo, o
    /// <see cref="MedicalHistoryErrors.NoAppointmentWithPatient"/> si no tiene ninguna cita con el paciente o este no
    /// existe.
    /// </returns>
    public async Task<Result<MedicalHistoryAccess>> ForCurrentDoctorAsync(
        int patientId, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor))
            return MedicalHistoryErrors.DoctorOnly;

        var doctorId = await doctors.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        // Un id no positivo no puede tener citas: se rechaza igual que un paciente sin relación, sin consultar.
        if (patientId <= 0
            || !await relationships.HasAppointmentWithPatientAsync(doctorId.Value, patientId, cancellationToken))
            return MedicalHistoryErrors.NoAppointmentWithPatient;

        return MedicalHistoryAccess.ForTreatingDoctor(doctorId.Value, patientId);
    }
}
