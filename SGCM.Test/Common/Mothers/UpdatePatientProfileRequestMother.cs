using SGCM.Web.Contracts.People;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="UpdatePatientProfileRequest"/>: el cuerpo que envía el cliente,
/// con los mismos datos nuevos de <see cref="UpdatePatientProfileCommandMother"/>.
/// </summary>
public static class UpdatePatientProfileRequestMother
{
    /// <summary>Teléfono nuevo.</summary>
    public const string NewPhoneNumber = UpdatePatientProfileCommandMother.NewPhoneNumber;

    /// <summary>Dirección nueva.</summary>
    public const string NewAddress = UpdatePatientProfileCommandMother.NewAddress;

    /// <summary>Nombre del contacto de emergencia nuevo.</summary>
    public const string NewEmergencyContactName = UpdatePatientProfileCommandMother.NewEmergencyContactName;

    /// <summary>Teléfono del contacto de emergencia nuevo.</summary>
    public const string NewEmergencyContactPhone = UpdatePatientProfileCommandMother.NewEmergencyContactPhone;

    /// <summary>Número de póliza nuevo.</summary>
    public const string NewPolicyNumber = UpdatePatientProfileCommandMother.NewPolicyNumber;

    /// <summary>
    /// Solo el teléfono: quita la dirección, el contacto de emergencia y el seguro.
    /// </summary>
    public static UpdatePatientProfileRequest WithRequiredData() => new(
        NewPhoneNumber,
        Address: null,
        EmergencyContactName: null,
        EmergencyContactPhone: null,
        InsuranceProviderId: null,
        PolicyNumber: null);

    /// <summary>
    /// Todos los datos editables, incluidos el contacto de emergencia y el seguro.
    /// </summary>
    /// <param name="insuranceProviderId">Id del proveedor de seguros.</param>
    public static UpdatePatientProfileRequest WithAllData(int insuranceProviderId) => WithRequiredData() with
    {
        Address = NewAddress,
        EmergencyContactName = NewEmergencyContactName,
        EmergencyContactPhone = NewEmergencyContactPhone,
        InsuranceProviderId = insuranceProviderId,
        PolicyNumber = NewPolicyNumber
    };
}
