namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Una página de la agenda del médico.</summary>
public sealed record DoctorScheduleResponse(
    IReadOnlyList<DoctorScheduleItemResponse> Items,
    int PageNumber,
    int PageSize,
    int TotalCount);
