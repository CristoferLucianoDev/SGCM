
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.Patients;

/// <summary>
/// Errores de negocio relacionados con el perfil del paciente.
/// </summary>
public static class PatientErrors
{
    public static readonly Error ProfileAlreadyExists =
        Error.Conflict(
            "Patient.ProfileAlreadyExists",
            "El usuario ya tiene un perfil de paciente.");

    public static readonly Error ProfileNotFound =
        Error.NotFound(
            "Patient.ProfileNotFound",
            "El usuario todavía no ha completado su perfil de paciente.");

    public static readonly Error InvalidUserId =
        Error.Validation(
            "Patient.InvalidUserId",
            "El usuario asociado al paciente no es válido.");

    public static readonly Error FirstNameRequired =
        Error.Validation(
            "Patient.FirstNameRequired",
            "El nombre del paciente es obligatorio.");
	
	
public static readonly Error IncompleteEmergencyContact =
    	Error.Validation(
        "Patient.IncompleteEmergencyContact",
        "El contacto de emergencia debe incluir nombre y teléfono.");


    public static readonly Error FirstNameTooLong =
        Error.Validation(
            "Patient.FirstNameTooLong",
            "El nombre no puede superar los 100 caracteres.");

    public static readonly Error LastNameRequired =
        Error.Validation(
            "Patient.LastNameRequired",
            "El apellido del paciente es obligatorio.");

    public static readonly Error LastNameTooLong =
        Error.Validation(
            "Patient.LastNameTooLong",
            "El apellido no puede superar los 100 caracteres.");

    public static readonly Error InvalidBirthDate =
        Error.Validation(
            "Patient.InvalidBirthDate",
            "La fecha de nacimiento no es válida o es futura.");

    public static readonly Error PhoneRequired =
        Error.Validation(
            "Patient.PhoneRequired",
            "El teléfono del paciente es obligatorio.");

    public static readonly Error PhoneTooLong =
        Error.Validation(
            "Patient.PhoneTooLong",
            "El teléfono no puede superar los 20 caracteres.");

    public static readonly Error AddressTooLong =
        Error.Validation(
            "Patient.AddressTooLong",
            "La dirección no puede superar los 250 caracteres.");

    public static readonly Error EmergencyContactNameTooLong =
        Error.Validation(
            "Patient.EmergencyContactNameTooLong",
            "El nombre del contacto de emergencia no puede superar los 150 caracteres.");

    public static readonly Error EmergencyContactPhoneTooLong =
        Error.Validation(
            "Patient.EmergencyContactPhoneTooLong",
            "El teléfono de emergencia no puede superar los 20 caracteres.");

    public static readonly Error InvalidInsuranceProvider =
        Error.Validation(
            "Patient.InvalidInsuranceProvider",
            "El proveedor de seguros seleccionado no es válido.");

    public static readonly Error PolicyNumberRequired =
        Error.Validation(
            "Patient.PolicyNumberRequired",
            "Debe proporcionar un número de póliza cuando selecciona un seguro médico.");

    public static readonly Error PolicyNumberTooLong =
        Error.Validation(
            "Patient.PolicyNumberTooLong",
            "El número de póliza no puede superar los 50 caracteres.");
}
