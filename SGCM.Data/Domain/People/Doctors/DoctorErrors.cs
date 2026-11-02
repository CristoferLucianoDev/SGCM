using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.Doctors;

public static class DoctorErrors
{
    public static readonly Error SpecialtyAlreadyAssociated = Error.Conflict(
        "Doctor.SpecialtyAlreadyAssociated", "El médico ya tiene asociada esta especialidad.");

    public static readonly Error SpecialtyNotAssociated = Error.Conflict(
        "Doctor.SpecialtyNotAssociated", "El médico no tiene asociada esta especialidad.");

    public static readonly Error CannotRemoveLastSpecialty = Error.Conflict(
        "Doctor.CannotRemoveLastSpecialty", "El médico debe conservar al menos una especialidad.");

    public static readonly Error SpecialtyRequired = Error.Validation(
        "Doctor.SpecialtyRequired", "El perfil del médico debe incluir al menos una especialidad.");

    public static readonly Error InvalidUserId = Error.Validation(
        "Doctor.InvalidUserId", "El usuario asociado al médico debe tener un identificador mayor que cero.");

    public static readonly Error InvalidProvinceId = Error.Validation(
        "Doctor.InvalidProvinceId", "El identificador de la provincia debe ser mayor que cero.");

    public static readonly Error InvalidMunicipalityId = Error.Validation(
        "Doctor.InvalidMunicipalityId", "El identificador del municipio debe ser mayor que cero.");

    public static readonly Error InvalidConsultationFee = Error.Validation(
        "Doctor.InvalidConsultationFee", "La tarifa debe estar entre 0 y 99999999.99.");

    public static readonly Error InvalidYearsOfExperience = Error.Validation(
        "Doctor.InvalidYearsOfExperience", "Los años de experiencia no pueden ser negativos.");

    public static readonly Error FirstNameRequired = Error.Validation(
        "Doctor.FirstNameRequired", "El nombre del médico es obligatorio.");

    public static readonly Error FirstNameTooLong = Error.Validation(
        "Doctor.FirstNameTooLong", $"El nombre no puede superar {Doctor.MaxFirstNameLength} caracteres.");

    public static readonly Error LastNameRequired = Error.Validation(
        "Doctor.LastNameRequired", "El apellido del médico es obligatorio.");

    public static readonly Error LastNameTooLong = Error.Validation(
        "Doctor.LastNameTooLong", $"El apellido no puede superar {Doctor.MaxLastNameLength} caracteres.");

    public static readonly Error LicenseNumberRequired = Error.Validation(
        "Doctor.LicenseNumberRequired", "El número de licencia del médico es obligatorio.");

    public static readonly Error LicenseNumberTooLong = Error.Validation(
        "Doctor.LicenseNumberTooLong", $"El número de licencia no puede superar {Doctor.MaxLicenseNumberLength} caracteres.");

    public static readonly Error PhoneNumberRequired = Error.Validation(
        "Doctor.PhoneNumberRequired", "El teléfono del médico es obligatorio.");

    public static readonly Error PhoneNumberTooLong = Error.Validation(
        "Doctor.PhoneNumberTooLong", $"El teléfono no puede superar {Doctor.MaxPhoneNumberLength} caracteres.");

    public static readonly Error AddressLineRequired = Error.Validation(
        "Doctor.AddressLineRequired", "El domicilio del médico es obligatorio.");

    public static readonly Error AddressLineTooLong = Error.Validation(
        "Doctor.AddressLineTooLong", $"El domicilio no puede superar {Doctor.MaxAddressLineLength} caracteres.");

    public static readonly Error BiographyRequired = Error.Validation(
        "Doctor.BiographyRequired", "La biografía del médico es obligatoria.");

    public static readonly Error BiographyTooLong = Error.Validation(
        "Doctor.BiographyTooLong", $"La biografía no puede superar {Doctor.MaxBiographyLength} caracteres.");
}
