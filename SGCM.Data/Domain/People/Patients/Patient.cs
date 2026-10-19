
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Domain.People.Patients;

/// <summary>
/// Perfil de un paciente del sistema SGCM.
/// Los datos de identidad se establecen al crear el perfil
/// y no pueden ser modificados directamente por el paciente.
/// </summary>
public sealed class Patient : ActivatableAggregateRoot
{
    public int UserId { get; private set; }

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public DateOnly BirthDate { get; private set; }

    public string PhoneNumber { get; private set; } = null!;
    public string? Address { get; private set; }

    public string? EmergencyContactName { get; private set; }
    public string? EmergencyContactPhone { get; private set; }

    public int? InsuranceProviderId { get; private set; }
    public string? PolicyNumber { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public int? CreatedBy { get; private set; }

    public DateTimeOffset? UpdatedAt { get; private set; }
    public int? UpdatedBy { get; private set; }

    protected override string EntityName => nameof(Patient);

    // Constructor utilizado por EF Core al recuperar el perfil.
    private Patient()
    {
    }

    /// <summary>
    /// Crea un perfil de paciente validando sus datos obligatorios,
    /// las longitudes permitidas y las reglas del seguro médico.
    /// </summary>
    public static Result<Patient> Create(
        int userId,
        string? firstName,
        string? lastName,
        DateOnly birthDate,
        string? phoneNumber,
        string? address,
        string? emergencyContactName,
        string? emergencyContactPhone,
        int? insuranceProviderId,
        string? policyNumber,
        DateTimeOffset now)
    {
        if (userId <= 0)
            return PatientErrors.InvalidUserId;

        if (string.IsNullOrWhiteSpace(firstName))
            return PatientErrors.FirstNameRequired;

        firstName = firstName.Trim();

        if (firstName.Length > 100)
            return PatientErrors.FirstNameTooLong;

        if (string.IsNullOrWhiteSpace(lastName))
            return PatientErrors.LastNameRequired;

        lastName = lastName.Trim();

        if (lastName.Length > 100)
            return PatientErrors.LastNameTooLong;

        if (birthDate == default ||
            birthDate > DateOnly.FromDateTime(now.UtcDateTime))
            return PatientErrors.InvalidBirthDate;

        if (string.IsNullOrWhiteSpace(phoneNumber))
            return PatientErrors.PhoneRequired;

        phoneNumber = phoneNumber.Trim();

        if (phoneNumber.Length > 20)
            return PatientErrors.PhoneTooLong;

        address = NormalizeOptional(address);

        if (address?.Length > 250)
            return PatientErrors.AddressTooLong;

        emergencyContactName = NormalizeOptional(emergencyContactName);

        if (emergencyContactName?.Length > 150)
            return PatientErrors.EmergencyContactNameTooLong;

        emergencyContactPhone = NormalizeOptional(emergencyContactPhone);

        if (emergencyContactPhone?.Length > 20)
            return PatientErrors.EmergencyContactPhoneTooLong;

        if (insuranceProviderId is <= 0)
            return PatientErrors.InvalidInsuranceProvider;

        policyNumber = NormalizeOptional(policyNumber);

        if (insuranceProviderId.HasValue &&
            string.IsNullOrWhiteSpace(policyNumber))
            return PatientErrors.PolicyNumberRequired;

        if (policyNumber?.Length > 50)
            return PatientErrors.PolicyNumberTooLong;

        var patient = new Patient
        {
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            BirthDate = birthDate,
            PhoneNumber = phoneNumber,
            Address = address,
            EmergencyContactName = emergencyContactName,
            EmergencyContactPhone = emergencyContactPhone,
            InsuranceProviderId = insuranceProviderId,
            PolicyNumber = policyNumber,
            CreatedAt = now,
            CreatedBy = userId
        };

        return patient;
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
