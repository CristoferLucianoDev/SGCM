using SGCM.Web.Contracts.People;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="CompletePatientProfileRequest"/>: el cuerpo que envía el cliente,
/// con los mismos datos de <see cref="PatientMother"/>.
/// </summary>
public static class CompletePatientProfileRequestMother
{
    /// <summary>
    /// Solicitud solo con los datos obligatorios: sin dirección, contacto de emergencia ni seguro.
    /// </summary>
    public static CompletePatientProfileRequest WithRequiredData() => new(
        PatientMother.DefaultFirstName,
        PatientMother.DefaultLastName,
        PatientMother.DefaultBirthDate,
        PatientMother.DefaultPhoneNumber,
        Address: null,
        EmergencyContactName: null,
        EmergencyContactPhone: null,
        InsuranceProviderId: null,
        PolicyNumber: null);

    /// <summary>
    /// Solicitud con todos los datos, incluidos el contacto de emergencia y el seguro.
    /// </summary>
    /// <param name="insuranceProviderId">Id del proveedor de seguros.</param>
    public static CompletePatientProfileRequest WithAllData(int insuranceProviderId) => WithRequiredData() with
    {
        Address = PatientMother.DefaultAddress,
        EmergencyContactName = PatientMother.DefaultEmergencyContactName,
        EmergencyContactPhone = PatientMother.DefaultEmergencyContactPhone,
        InsuranceProviderId = insuranceProviderId,
        PolicyNumber = PatientMother.DefaultPolicyNumber
    };
}
