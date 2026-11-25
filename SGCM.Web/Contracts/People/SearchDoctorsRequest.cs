namespace SGCM.Web.Contracts.People;

public sealed record SearchDoctorsRequest(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20,
    int? SpecialtyId = null,
    int? ProvinceId = null,
    int? MunicipalityId = null);
