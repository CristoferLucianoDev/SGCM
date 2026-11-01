using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed record SpecialtyResponse(
    int Id,
    string Name,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsActive);
