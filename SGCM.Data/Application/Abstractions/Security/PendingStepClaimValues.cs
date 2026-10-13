using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Application.Abstractions.Security;

/// <summary>
/// Valores estables del claim <see cref="AppClaims.PendingStep"/>. El mapeo es explícito
/// para que renombrar un valor de <see cref="PendingStep"/> no cambie lo que lee el filtro.
/// </summary>
public static class PendingStepClaimValues
{
    /// <summary>Cambiar la contraseña temporal.</summary>
    public const string ChangeTemporaryPassword = "change_password";

    /// <summary>Completar el perfil del médico.</summary>
    public const string DoctorProfile = "doctor_profile";

    /// <summary>Completar el perfil del paciente.</summary>
    public const string PatientProfile = "patient_profile";

    /// <summary>
    /// Devuelve el valor del claim que corresponde al paso indicado.
    /// </summary>
    /// <param name="step">Paso pendiente del usuario.</param>
    /// <returns>El texto que viaja en el claim.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Si el paso no tiene un valor de claim definido.</exception>
    public static string From(PendingStep step) => step switch
    {
        PendingStep.ChangeTemporaryPassword => ChangeTemporaryPassword,
        PendingStep.DoctorProfile => DoctorProfile,
        PendingStep.PatientProfile => PatientProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "El paso pendiente no tiene un valor de claim definido.")
    };
}
