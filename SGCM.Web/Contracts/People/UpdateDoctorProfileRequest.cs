using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

/// <summary>Datos profesionales editables del médico autenticado.</summary>
public sealed record UpdateDoctorProfileRequest(
    string? PhoneNumber,
    int ProvinceId,
    int MunicipalityId,
    string? AddressLine,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] decimal ConsultationFee,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.Strict)] int YearsOfExperience,
    string? Biography);
