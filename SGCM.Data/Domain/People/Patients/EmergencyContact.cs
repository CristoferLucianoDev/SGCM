
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.Patients;

/// <summary>
/// Objeto de valor que representa el contacto de emergencia.
/// El nombre y el teléfono deben proporcionarse juntos.
/// </summary>
public sealed record EmergencyContact
{
    public string Name { get; }
    public string PhoneNumber { get; }

    private EmergencyContact(string name, string phoneNumber)
    {
        Name = name;
        PhoneNumber = phoneNumber;
    }

    public static Result<EmergencyContact> Create(
        string? name,
        string? phoneNumber)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(phoneNumber))
        {
            return PatientErrors.IncompleteEmergencyContact;
        }

        name = name.Trim();
        phoneNumber = phoneNumber.Trim();

        if (name.Length > 150)
            return PatientErrors.EmergencyContactNameTooLong;

        if (phoneNumber.Length > 20)
            return PatientErrors.EmergencyContactPhoneTooLong;

        return new EmergencyContact(name, phoneNumber);
    }
}
