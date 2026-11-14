namespace SGCM.Web.Contracts.People;

/// <summary>
/// Datos editables del perfil del paciente. Reemplaza los valores actuales: un campo opcional en
/// <c>null</c> lo quita, e <see cref="InsuranceProviderId"/> en <c>null</c> quita el seguro.
/// Nombre, apellido y fecha de nacimiento no se incluyen.
/// </summary>
public sealed record UpdatePatientProfileRequest(
    string PhoneNumber,
    string? Address,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    int? InsuranceProviderId,
    string? PolicyNumber);
