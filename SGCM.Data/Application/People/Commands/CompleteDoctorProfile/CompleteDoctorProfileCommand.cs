using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.People.Commands.CompleteDoctorProfile;

public sealed record CompleteDoctorProfileCommand(
    string? FirstName,
    string? LastName,
    string? LicenseNumber,
    string? PhoneNumber,
    int ProvinceId,
    int MunicipalityId,
    string? AddressLine,
    decimal ConsultationFee,
    int YearsOfExperience,
    string? Biography,
    IReadOnlyList<int>? SpecialtyIds,
    string? DeviceName,
    string? IpAddress) : ICommand<SessionTokens>
{
    internal Result<Doctor> CreateDoctor(int userId) =>
        Doctor.Create(userId, FirstName, LastName, LicenseNumber, PhoneNumber, ProvinceId, MunicipalityId,
            AddressLine, ConsultationFee, YearsOfExperience, Biography, SpecialtyIds);
}
