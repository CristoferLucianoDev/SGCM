using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed record ListSpecialtiesResponse(
    IReadOnlyList<SpecialtyResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Message = null);
