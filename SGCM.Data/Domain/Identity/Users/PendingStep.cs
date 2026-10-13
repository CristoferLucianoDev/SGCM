namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Paso que el usuario debe completar antes de usar el resto del sistema.
/// </summary>
public enum PendingStep
{
    /// <summary>Cambiar la contraseña temporal con la que se creó la cuenta.</summary>
    ChangeTemporaryPassword,

    /// <summary>Completar el perfil profesional del médico.</summary>
    DoctorProfile,

    /// <summary>Completar el perfil del paciente.</summary>
    PatientProfile
}
