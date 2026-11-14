using SGCM.Data.Application.People.Commands.UpdatePatientProfile;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="UpdatePatientProfileCommand"/>: comandos válidos con datos distintos
/// a los de <see cref="PatientMother"/>, para que cada prueba vea el cambio y altere solo el campo
/// que le importa.
/// </summary>
public static class UpdatePatientProfileCommandMother
{
    /// <summary>Teléfono nuevo.</summary>
    public const string NewPhoneNumber = "8295550111";

    /// <summary>Dirección nueva.</summary>
    public const string NewAddress = "Av. Independencia 25, Santo Domingo";

    /// <summary>Nombre del contacto de emergencia nuevo.</summary>
    public const string NewEmergencyContactName = "María Gómez";

    /// <summary>Teléfono del contacto de emergencia nuevo.</summary>
    public const string NewEmergencyContactPhone = "8495550122";

    /// <summary>Número de póliza nuevo.</summary>
    public const string NewPolicyNumber = "POL-9999";

    /// <summary>
    /// Solo el teléfono: sin dirección, contacto de emergencia ni seguro.
    /// </summary>
    public static UpdatePatientProfileCommand WithRequiredData() => new(
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
    public static UpdatePatientProfileCommand WithAllData(int insuranceProviderId) => WithRequiredData() with
    {
        Address = NewAddress,
        EmergencyContactName = NewEmergencyContactName,
        EmergencyContactPhone = NewEmergencyContactPhone,
        InsuranceProviderId = insuranceProviderId,
        PolicyNumber = NewPolicyNumber
    };
}
