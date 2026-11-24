using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Queries.GetAffectedAppointments;

/// <summary>
/// Calcula los rangos que dejan de estar disponibles (el bloque entero al cerrar, o lo que queda fuera del horario
/// nuevo al reprogramar) y aplica la política de citas afectadas. Un bloque de otro médico se trata como inexistente.
/// </summary>
internal sealed class GetAffectedAppointmentsQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IDoctorAvailabilityRepository availabilityRepository,
    IAffectedAppointmentsPolicy policy,
    IValidator<GetAffectedAppointmentsQuery> validator) : IQueryHandler<GetAffectedAppointmentsQuery, AffectedAppointmentsDto>
{
    public async Task<Result<AffectedAppointmentsDto>> HandleAsync(
        GetAffectedAppointmentsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return DoctorErrors.NotFound;
        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        var block = await availabilityRepository.GetByIdAsync(query.BlockId, cancellationToken);
        if (block is null || block.DoctorId != doctorId) return DoctorAvailabilityErrors.NotFound;

        var removed = RemovedRanges(block, query);
        if (removed.IsFailure) return removed.Error;

        var affected = await policy.EvaluateAsync(doctorId.Value, removed.Value, cancellationToken);

        return new AffectedAppointmentsDto(
            affected.Appointments.Count,
            affected.IsReasonRequired,
            affected.IsShortNotice,
            affected.ShortNotice.ThresholdHours,
            affected.ShortNotice.MinimumWords);
    }

    private static Result<IReadOnlyCollection<TimeRange>> RemovedRanges(DoctorAvailability block, GetAffectedAppointmentsQuery query)
    {
        var blockRange = TimeRange.Create(block.StartsAt, block.EndsAt).Value;
        if (query.StartsAt is not { } startsAt || query.EndsAt is not { } endsAt)
            return Result<IReadOnlyCollection<TimeRange>>.Success([blockRange]);

        var newRange = TimeRange.Create(startsAt, endsAt);
        if (newRange.IsFailure) return DoctorAvailabilityErrors.InvalidTimeRange;

        return Result<IReadOnlyCollection<TimeRange>>.Success(blockRange.Subtract(newRange.Value).ToList());
    }
}
