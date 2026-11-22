using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed record DoctorPublicProfileResponse(
    [property: JsonRequired] int Id,
    [property: JsonRequired] string FullName,
    [property: JsonRequired] IReadOnlyList<DoctorPublicProfileResponse.Reference> Specialties,
    [property: JsonRequired] DoctorPublicProfileResponse.Reference Province,
    [property: JsonRequired] DoctorPublicProfileResponse.Reference Municipality,
    [property: JsonRequired] string AddressLine,
    [property: JsonRequired] decimal ConsultationFee,
    [property: JsonRequired] int YearsOfExperience,
    [property: JsonRequired] string Biography)
{
    public sealed record Reference([property: JsonRequired] int Id, [property: JsonRequired] string Name);
}
