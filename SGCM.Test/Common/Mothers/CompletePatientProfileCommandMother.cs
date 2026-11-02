using SGCM.Data.Application.People.Commands.CompletePatientProfile;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="CompletePatientProfileCommand"/>: comandos válidos con los datos
/// de <see cref="PatientMother"/>, para que cada prueba cambie solo el campo que le importa.
/// </summary>
public static class CompletePatientProfileCommandMother
{
    /// <summary>Dispositivo de los comandos creados.</summary>
    public const string DefaultDeviceName = "Chrome en Windows";

    /// <summary>IP de los comandos creados.</summary>
    public const string DefaultIpAddress = "203.0.113.10";

    /// <summary>
    /// Comando solo con los datos obligatorios: sin dirección, contacto de emergencia ni seguro.
    /// </summary>
    public static CompletePatientProfileCommand WithRequiredData() => new(
        PatientMother.DefaultFirstName,
        PatientMother.DefaultLastName,
        PatientMother.DefaultBirthDate,
        PatientMother.DefaultPhoneNumber,
        Address: null,
        EmergencyContactName: null,
        EmergencyContactPhone: null,
        InsuranceProviderId: null,
        PolicyNumber: null,
        DefaultDeviceName,
        DefaultIpAddress);

    /// <summary>
    /// Comando con todos los datos, incluidos el contacto de emergencia y el seguro.
    /// </summary>
    /// <param name="insuranceProviderId">Id del proveedor de seguros.</param>
    public static CompletePatientProfileCommand WithAllData(int insuranceProviderId) => WithRequiredData() with
    {
        Address = PatientMother.DefaultAddress,
        EmergencyContactName = PatientMother.DefaultEmergencyContactName,
        EmergencyContactPhone = PatientMother.DefaultEmergencyContactPhone,
        InsuranceProviderId = insuranceProviderId,
        PolicyNumber = PatientMother.DefaultPolicyNumber
    };
}
