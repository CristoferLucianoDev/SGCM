using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed record SearchDoctorsResponse(
    [property: JsonRequired] IReadOnlyList<DoctorSearchResponse> Items,
    [property: JsonRequired] int PageNumber,
    [property: JsonRequired] int PageSize,
    [property: JsonRequired] int TotalCount);
