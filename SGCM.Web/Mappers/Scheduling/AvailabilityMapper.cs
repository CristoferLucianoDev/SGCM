using SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;
using SGCM.Web.Contracts.Scheduling;

namespace SGCM.Web.Mappers.Scheduling;

public static class AvailabilityMapper
{
    public static CreateAvailabilitySeriesCommand ToCommand(this CreateAvailabilitySeriesRequest request) =>
        new(request.DaysOfWeek, request.StartTime, request.EndTime, request.StartDate, request.EndDate);

    public static CreateAvailabilityBlockCommand ToCommand(this CreateAvailabilityBlockRequest request) =>
        new(request.Date, request.StartTime, request.EndTime);

    public static AffectedAppointmentsResponse ToResponse(this AffectedAppointmentsDto affected) =>
        new(affected.Count, affected.IsReasonRequired, affected.IsShortNotice,
            affected.ShortNoticeThresholdHours, affected.MinimumShortNoticeWords);

    public static CloseAvailabilityBlockCommand ToCommand(this CloseAvailabilityBlockRequest request, int blockId) =>
        new(blockId, request.Reason);

    public static CloseAvailabilityBlockResponse ToResponse(this ClosedAvailabilityBlockDto closed) =>
        new(closed.CancelledAppointments);
}
