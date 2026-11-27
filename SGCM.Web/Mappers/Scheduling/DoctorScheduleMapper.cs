using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Contracts.Scheduling;

namespace SGCM.Web.Mappers.Scheduling;

public static class DoctorScheduleMapper
{
    public static GetDoctorScheduleQuery ToQuery(this DoctorScheduleRequest request) => new(
        request.Status, request.From, request.To, request.PatientName, request.Sort, request.PageNumber, request.PageSize);

    public static DoctorScheduleItemResponse ToResponse(this DoctorScheduleItemDto item) => new(
        item.AppointmentId,
        item.PatientId,
        item.PatientName,
        item.SpecialtyId,
        item.SpecialtyName,
        item.StartsAt,
        item.EndsAt,
        item.AppointmentDate,
        item.StartTime,
        item.EndTime,
        ToText(item.Status),
        item.IsInProgress);

    public static DoctorScheduleResponse ToResponse(this PagedResult<DoctorScheduleItemDto> page) => new(
        page.Items.Select(item => item.ToResponse()).ToArray(), page.PageNumber, page.PageSize, page.TotalCount);

    public static DoctorCalendarResponse ToCalendarResponse(this IReadOnlyList<DoctorScheduleItemDto> items) =>
        new(items.Select(item => item.ToResponse()).ToArray());

    public static DoctorAppointmentDetailResponse ToResponse(this DoctorAppointmentDetailDto detail) => new(
        detail.AppointmentId,
        detail.PatientId,
        detail.PatientName,
        detail.PatientPhoneNumber,
        detail.PatientEmail,
        detail.SpecialtyId,
        detail.SpecialtyName,
        detail.StartsAt,
        detail.EndsAt,
        detail.AppointmentDate,
        detail.StartTime,
        detail.EndTime,
        ToText(detail.Status),
        detail.IsInProgress,
        detail.ReasonForVisit);

    // Igual que en la base: el estado en minúsculas.
    private static string ToText(AppointmentStatus status) => status.ToString().ToLowerInvariant();
}
