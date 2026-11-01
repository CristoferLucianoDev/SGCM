namespace SGCM.Web.Contracts.People;

public sealed record ListSpecialtiesRequest(
    string? Search = null,
    int PageNumber = 1,
    int PageSize = 20,
    SpecialtyStatusFilter Status = SpecialtyStatusFilter.Active);
