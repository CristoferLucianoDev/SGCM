namespace SGCM.Data.Application.People.Queries.GetDoctorProfile;

public sealed record DoctorAdminProfileDto(
    DoctorPublicProfileDto Profile,
    string PhoneNumber,
    string LicenseNumber) : DoctorProfileResult;
