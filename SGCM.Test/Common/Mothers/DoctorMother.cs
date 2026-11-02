using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Test.Common.Mothers;

public static class DoctorMother
{
    public const int UserId = 42;
    public const string FirstName = "Luis";
    public const string LastName = "Pérez";
    public const string LicenseNumber = "LIC-227";
    public const string PhoneNumber = "809-555-0100";
    public const int ProvinceId = 3;
    public const int MunicipalityId = 7;
    public const string AddressLine = "Calle Principal 227";
    public const decimal ConsultationFee = 1500.50m;
    public const int YearsOfExperience = 10;
    public const string Biography = "Atención MÉDICA.\nPara la niñez.";

    public static Result<Doctor> Create(
        IEnumerable<int>? specialtyIds,
        int userId = UserId,
        string? firstName = FirstName,
        string? lastName = LastName,
        string? licenseNumber = LicenseNumber,
        string? phoneNumber = PhoneNumber,
        int provinceId = ProvinceId,
        int municipalityId = MunicipalityId,
        string? addressLine = AddressLine,
        decimal consultationFee = ConsultationFee,
        int yearsOfExperience = YearsOfExperience,
        string? biography = Biography) =>
        Doctor.Create(userId, firstName, lastName, licenseNumber, phoneNumber,
            provinceId, municipalityId, addressLine, consultationFee, yearsOfExperience, biography, specialtyIds);
}
