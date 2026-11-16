using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

/// <summary>Datos del perfil. El médico se identifica mediante la sesión autenticada.</summary>
public sealed record CompleteDoctorProfileRequest(
    string? FirstName,
    string? LastName,
    string? LicenseNumber,
    string? PhoneNumber,
    int ProvinceId,
    int MunicipalityId,
    string? AddressLine,
    [property: JsonRequired] decimal ConsultationFee,
    [property: JsonRequired] int YearsOfExperience,
    string? Biography,
    IReadOnlyList<int>? SpecialtyIds);
