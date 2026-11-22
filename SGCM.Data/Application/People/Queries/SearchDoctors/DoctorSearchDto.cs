namespace SGCM.Data.Application.People.Queries.SearchDoctors;

/// <summary>Datos del médico visibles en el directorio.</summary>
public sealed record DoctorSearchDto(
    int Id,
    string FirstName,
    string LastName,
    int ProvinceId,
    string ProvinceName,
    int MunicipalityId,
    string MunicipalityName,
    string AddressLine,
    decimal ConsultationFee,
    int YearsOfExperience,
    string Biography,
    int[] SpecialtyIds,
    string[] SpecialtyNames);
