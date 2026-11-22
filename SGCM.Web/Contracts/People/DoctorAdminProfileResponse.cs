using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed record DoctorAdminProfileResponse(
    [property: JsonRequired] DoctorPublicProfileResponse Profile,
    [property: JsonRequired] string PhoneNumber,
    [property: JsonRequired] string LicenseNumber);
