using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Application.People.Commands.UpdatePatientProfile;
using SGCM.Web.Contracts.People;

namespace SGCM.Web.Mappers.People;

public static class PatientMapper
{
    public static CompletePatientProfileCommand ToCommand(
        this CompletePatientProfileRequest request,
        string? deviceName,
        string? ipAddress) =>
        new(
            request.FirstName,
            request.LastName,
            request.BirthDate,
            request.PhoneNumber,
            request.Address,
            request.EmergencyContactName,
            request.EmergencyContactPhone,
            request.InsuranceProviderId,
            request.PolicyNumber,
            deviceName,
            ipAddress);

    public static UpdatePatientProfileCommand ToCommand(this UpdatePatientProfileRequest request) =>
        new(
            request.PhoneNumber,
            request.Address,
            request.EmergencyContactName,
            request.EmergencyContactPhone,
            request.InsuranceProviderId,
            request.PolicyNumber);
}
