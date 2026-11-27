using SGCM.Data.Application.Scheduling.Commands.CloseAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.CreateAvailabilitySeries;
using SGCM.Data.Application.Scheduling.Commands.RescheduleAvailabilityBlock;
using SGCM.Data.Application.Scheduling.Commands.UpdateAvailabilitySeries;
using SGCM.Data.Application.Scheduling.Queries.GetAvailabilityBlocks;
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

    public static RescheduleAvailabilityBlockCommand ToCommand(this RescheduleAvailabilityBlockRequest request, int blockId) =>
        new(blockId, request.Date, request.StartTime, request.EndTime, request.Reason);

    public static RescheduleAvailabilityBlockResponse ToResponse(this RescheduledAvailabilityBlockDto rescheduled) =>
        new(rescheduled.CancelledAppointments);

    public static UpdateAvailabilitySeriesCommand ToCommand(this UpdateAvailabilitySeriesRequest request, int seriesId) =>
        new(seriesId, request.DaysOfWeek, request.StartTime, request.EndTime, request.EndDate, request.Reason);

    public static UpdateAvailabilitySeriesResponse ToResponse(this UpdatedAvailabilitySeriesDto updated) =>
        new(updated.EffectiveFrom, updated.CancelledAppointments);

    public static AvailabilityBlocksResponse ToResponse(this IReadOnlyList<AvailabilityBlockDto> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);

        return new AvailabilityBlocksResponse(blocks
            .Select(block => new AvailabilityBlockResponse(
                block.Id, block.SeriesId, block.StartsAt, block.EndsAt, block.IsAvailable, block.IsModified, block.CanReschedule))
            .ToArray());
    }
}
