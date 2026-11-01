namespace SGCM.Web.Contracts.People;

/// <summary>
/// Datos del perfil del paciente. El paciente sale del token; el dispositivo y la IP, de la petición.
/// </summary>
public sealed record CompletePatientProfileRequest(
    string FirstName,
    string LastName,
    DateOnly BirthDate,
    string PhoneNumber,
    string? Address,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    int? InsuranceProviderId,
    string? PolicyNumber);
