using SGCM.Data.Domain.People.Patients;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="Patient"/>: crea perfiles válidos con datos fijos, para que
/// las pruebas solo declaren lo que les importa.
/// </summary>
public static class PatientMother
{
    /// <summary>Nombre de los pacientes creados.</summary>
    public const string DefaultFirstName = "Ana";

    /// <summary>Apellido de los pacientes creados.</summary>
    public const string DefaultLastName = "Pérez";

    /// <summary>Teléfono de los pacientes creados.</summary>
    public const string DefaultPhoneNumber = "8095550100";

    /// <summary>Dirección de los pacientes con todos los datos.</summary>
    public const string DefaultAddress = "Calle Principal 10, Santo Domingo";

    /// <summary>Nombre del contacto de emergencia de los pacientes con todos los datos.</summary>
    public const string DefaultEmergencyContactName = "Luis Pérez";

    /// <summary>Teléfono del contacto de emergencia de los pacientes con todos los datos.</summary>
    public const string DefaultEmergencyContactPhone = "8095550199";

    /// <summary>Número de póliza de los pacientes con seguro.</summary>
    public const string DefaultPolicyNumber = "POL-0001";

    /// <summary>Fecha de nacimiento: treinta años antes de <see cref="UserMother.Now"/>.</summary>
    public static readonly DateOnly DefaultBirthDate =
        DateOnly.FromDateTime(UserMother.Now.UtcDateTime).AddYears(-30);

    /// <summary>
    /// Paciente solo con los datos obligatorios: sin dirección, contacto de emergencia ni seguro.
    /// </summary>
    /// <param name="userId">Id del usuario dueño del perfil.</param>
    public static Patient WithRequiredData(int userId) =>
        Patient.Create(
            userId,
            DefaultFirstName,
            DefaultLastName,
            DefaultBirthDate,
            DefaultPhoneNumber,
            address: null,
            emergencyContactName: null,
            emergencyContactPhone: null,
            insuranceProviderId: null,
            policyNumber: null,
            UserMother.Now).Value;

    /// <summary>
    /// Paciente con todos los datos, incluidos el contacto de emergencia y el seguro.
    /// </summary>
    /// <param name="userId">Id del usuario dueño del perfil.</param>
    /// <param name="insuranceProviderId">Id de un proveedor de seguros existente.</param>
    public static Patient WithAllData(int userId, int insuranceProviderId) =>
        Patient.Create(
            userId,
            DefaultFirstName,
            DefaultLastName,
            DefaultBirthDate,
            DefaultPhoneNumber,
            DefaultAddress,
            DefaultEmergencyContactName,
            DefaultEmergencyContactPhone,
            insuranceProviderId,
            DefaultPolicyNumber,
            UserMother.Now).Value;
}
