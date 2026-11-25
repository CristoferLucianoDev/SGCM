using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed record DoctorSearchResponse(
    [property: JsonRequired] int Id,
    [property: JsonRequired] string FullName,
    [property: JsonRequired] IReadOnlyList<DoctorPublicProfileResponse.Reference> Specialties,
    [property: JsonRequired] ProvinceResponse Province,
    [property: JsonRequired] MunicipalityResponse Municipality,
    [property: JsonRequired] decimal ConsultationFee);
