using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;

namespace SGCM.Data.Domain.People.Doctors;

/// <summary>Perfil profesional que conserva al menos una asociación con una especialidad.</summary>
public sealed class Doctor : ActivatableAggregateRoot
{
    public const int MaxFirstNameLength = 100;
    public const int MaxLastNameLength = 100;
    public const int MaxLicenseNumberLength = 50;
    public const int MaxPhoneNumberLength = 20;
    public const int MaxAddressLineLength = 250;
    public const int MaxBiographyLength = 1000;
    public const decimal MaxConsultationFee = 99999999.99m;

    private readonly List<DoctorSpecialty> _specialties = [];
    private readonly IReadOnlyCollection<DoctorSpecialty> _specialtiesView;

    public int UserId { get; private set; }
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string LicenseNumber { get; private set; } = null!;
    public string PhoneNumber { get; private set; } = null!;
    public int ProvinceId { get; private set; }
    public int MunicipalityId { get; private set; }
    public string AddressLine { get; private set; } = null!;
    public decimal ConsultationFee { get; private set; }
    public int YearsOfExperience { get; private set; }
    public string Biography { get; private set; } = null!;
    public IReadOnlyCollection<DoctorSpecialty> Specialties => _specialtiesView;

    protected override string EntityName => nameof(Doctor);

    private Doctor()
    {
        _specialtiesView = _specialties.AsReadOnly();
    }

    public static Result<Doctor> Create(
        int userId, string? firstName, string? lastName, string? licenseNumber, string? phoneNumber,
        int provinceId, int municipalityId, string? addressLine, decimal consultationFee,
        int yearsOfExperience, string? biography, IEnumerable<int>? specialtyIds)
    {
        if (userId <= 0) return DoctorErrors.InvalidUserId;

        var name = RequiredText(firstName, MaxFirstNameLength, DoctorErrors.FirstNameRequired, DoctorErrors.FirstNameTooLong);
        if (name.IsFailure) return name.Error;
        var surname = RequiredText(lastName, MaxLastNameLength, DoctorErrors.LastNameRequired, DoctorErrors.LastNameTooLong);
        if (surname.IsFailure) return surname.Error;
        var license = RequiredText(licenseNumber, MaxLicenseNumberLength, DoctorErrors.LicenseNumberRequired, DoctorErrors.LicenseNumberTooLong);
        if (license.IsFailure) return license.Error;
        var phone = RequiredText(phoneNumber, MaxPhoneNumberLength, DoctorErrors.PhoneNumberRequired, DoctorErrors.PhoneNumberTooLong);
        if (phone.IsFailure) return phone.Error;

        if (provinceId <= 0) return DoctorErrors.InvalidProvinceId;
        if (municipalityId <= 0) return DoctorErrors.InvalidMunicipalityId;

        var address = RequiredText(addressLine, MaxAddressLineLength, DoctorErrors.AddressLineRequired, DoctorErrors.AddressLineTooLong);
        if (address.IsFailure) return address.Error;
        if (consultationFee < 0 || consultationFee > MaxConsultationFee) return DoctorErrors.InvalidConsultationFee;
        if (yearsOfExperience < 0) return DoctorErrors.InvalidYearsOfExperience;
        var bio = RequiredText(biography, MaxBiographyLength, DoctorErrors.BiographyRequired, DoctorErrors.BiographyTooLong);
        if (bio.IsFailure) return bio.Error;

        if (specialtyIds is null) return DoctorErrors.SpecialtyRequired;
        var ids = specialtyIds.ToArray();
        if (ids.Length == 0) return DoctorErrors.SpecialtyRequired;
        if (ids.Any(id => id <= 0)) return SpecialtyErrors.InvalidId;
        if (ids.Distinct().Count() != ids.Length) return DoctorErrors.SpecialtyAlreadyAssociated;

        var doctor = new Doctor
        {
            UserId = userId,
            FirstName = name.Value,
            LastName = surname.Value,
            LicenseNumber = license.Value,
            PhoneNumber = phone.Value,
            ProvinceId = provinceId,
            MunicipalityId = municipalityId,
            AddressLine = address.Value,
            ConsultationFee = consultationFee,
            YearsOfExperience = yearsOfExperience,
            Biography = bio.Value
        };
        doctor._specialties.AddRange(ids.Select(id => new DoctorSpecialty(id)));
        return doctor;
    }

    public Result AssociateSpecialty(int specialtyId)
    {
        if (specialtyId <= 0) return SpecialtyErrors.InvalidId;
        if (_specialties.Any(link => link.SpecialtyId == specialtyId))
            return DoctorErrors.SpecialtyAlreadyAssociated;

        _specialties.Add(new DoctorSpecialty(specialtyId));
        return Result.Success();
    }

    public Result DisassociateSpecialty(int specialtyId)
    {
        if (specialtyId <= 0) return SpecialtyErrors.InvalidId;
        var association = _specialties.Find(link => link.SpecialtyId == specialtyId);
        if (association is null) return DoctorErrors.SpecialtyNotAssociated;
        if (_specialties.Count == 1) return DoctorErrors.CannotRemoveLastSpecialty;

        _specialties.Remove(association);
        return Result.Success();
    }

    private static Result<string> RequiredText(string? value, int maxLength, Error required, Error tooLong)
    {
        if (string.IsNullOrWhiteSpace(value)) return required;
        var trimmed = value.Trim();
        if (trimmed.Length > maxLength) return tooLong;
        return trimmed;
    }
}
