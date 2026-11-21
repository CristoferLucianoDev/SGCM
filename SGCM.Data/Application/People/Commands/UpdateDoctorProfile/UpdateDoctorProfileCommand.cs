using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.People.Commands.UpdateDoctorProfile;

public sealed record UpdateDoctorProfileCommand(
    string? PhoneNumber,
    int ProvinceId,
    int MunicipalityId,
    string? AddressLine,
    decimal ConsultationFee,
    int YearsOfExperience,
    string? Biography) : ICommand<Unit>;
