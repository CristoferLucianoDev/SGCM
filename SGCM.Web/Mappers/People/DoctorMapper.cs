using SGCM.Data.Application.People.Commands.CompleteDoctorProfile;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

public static class DoctorMapper
{
    public static CompleteDoctorProfileCommand ToCommand(
        this CompleteDoctorProfileRequest request, string? deviceName, string? ipAddress) =>
        new(request.FirstName, request.LastName, request.LicenseNumber, request.PhoneNumber, request.ProvinceId,
            request.MunicipalityId, request.AddressLine, request.ConsultationFee, request.YearsOfExperience,
            request.Biography, request.SpecialtyIds, deviceName, ipAddress);
}
