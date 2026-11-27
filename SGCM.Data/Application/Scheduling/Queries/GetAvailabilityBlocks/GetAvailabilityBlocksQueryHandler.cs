using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailabilityBlocks;

/// <summary>
/// Lista los bloques propios, abiertos y cerrados, que empiezan entre las dos fechas locales, en orden cronológico,
/// con su serie, si se editaron a mano y si hoy todavía pueden reprogramarse.
/// </summary>
internal sealed class GetAvailabilityBlocksQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IDoctorAvailabilityRepository availabilityRepository,
    BusinessCalendar calendar,
    IDateTimeProvider clock,
    IValidator<GetAvailabilityBlocksQuery> validator) : IQueryHandler<GetAvailabilityBlocksQuery, IReadOnlyList<AvailabilityBlockDto>>
{
    public async Task<Result<IReadOnlyList<AvailabilityBlockDto>>> HandleAsync(
        GetAvailabilityBlocksQuery query, CancellationToken cancellationToken)
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

        // Un día de margen a cada lado cubre la diferencia entre UTC y la zona de la agenda; se filtra por fecha local.
        var from = new DateTimeOffset(query.From.AddDays(-1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var to = new DateTimeOffset(query.To.AddDays(2).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var blocks = await availabilityRepository.ListByDoctorAsync(doctorId.Value, from, to, cancellationToken);

        var now = clock.UtcNow;
        var items = blocks
            .Where(block => IsWithin(calendar.ToLocalDate(block.StartsAt), query))
            .Select(block => new AvailabilityBlockDto(
                block.Id, block.SeriesId, block.StartsAt, block.EndsAt, block.IsAvailable, block.IsModified,
                block.CanReschedule(now, calendar)))
            .ToList();

        return Result<IReadOnlyList<AvailabilityBlockDto>>.Success(items);
    }

    private static bool IsWithin(DateOnly date, GetAvailabilityBlocksQuery query) => date >= query.From && date <= query.To;
}
