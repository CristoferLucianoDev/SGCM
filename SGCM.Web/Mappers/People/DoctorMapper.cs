using SGCM.Data.Application.People.Commands.CompleteDoctorProfile;
using SGCM.Data.Application.People.Commands.UpdateDoctorProfile;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

public static class DoctorMapper
{
    public static CompleteDoctorProfileCommand ToCommand(
        this CompleteDoctorProfileRequest request, string? deviceName, string? ipAddress) =>
        new(request.FirstName, request.LastName, request.LicenseNumber, request.PhoneNumber, request.ProvinceId,
            request.MunicipalityId, request.AddressLine, request.ConsultationFee, request.YearsOfExperience,
            request.Biography, request.SpecialtyIds, deviceName, ipAddress);

    public static UpdateDoctorProfileCommand ToCommand(this UpdateDoctorProfileRequest request) =>
        new(request.PhoneNumber, request.ProvinceId, request.MunicipalityId, request.AddressLine,
            request.ConsultationFee, request.YearsOfExperience, request.Biography);

    public static DoctorPublicProfileResponse ToResponse(this DoctorPublicProfileDto profile) =>
        new(profile.Id, profile.FullName, profile.Specialties.Select(ToReference).ToArray(),
            ToReference(profile.Province), ToReference(profile.Municipality), profile.AddressLine,
            profile.ConsultationFee, profile.YearsOfExperience, profile.Biography);

    public static DoctorAdminProfileResponse ToResponse(this DoctorAdminProfileDto profile) =>
        new(profile.Profile.ToResponse(), profile.PhoneNumber, profile.LicenseNumber);

    private static DoctorPublicProfileResponse.Reference ToReference(DoctorPublicProfileDto.Reference reference) =>
        new(reference.Id, reference.Name);
}
