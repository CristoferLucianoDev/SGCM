using FluentValidation;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;

/// <summary>
/// Calcula los horarios libres, con dos formas de consulta: de un médico concreto, o de una especialidad cuando
/// el paciente no elige médico, caso en que se ofrece cada hora en que al menos un médico activo de esa
/// especialidad, dentro de los filtros de ubicación, la tiene libre. La ventana depende de quién consulta: el
/// paciente ve desde la antelación mínima hasta el horizonte de reservas y el médico ve los suyos desde el
/// instante actual. Los bloques y las citas se leen una sola vez para todos los candidatos, en lecturas
/// secuenciales porque comparten el mismo contexto.
/// </summary>
internal sealed class GetAvailableSlotsQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IDoctorAvailabilityRepository availabilityRepository,
    IActiveAppointmentReader appointmentReader,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions,
    IValidator<GetAvailableSlotsQuery> validator)
    : IQueryHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>>
{
    public async Task<Result<IReadOnlyList<AvailableSlotDto>>> HandleAsync(
        GetAvailableSlotsQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        var isDoctor = IsAuthenticatedIn(UserRole.Doctor);
        if (!isDoctor && !IsAuthenticatedIn(UserRole.Patient)) return AvailableSlotsErrors.RoleNotAllowed;

        var candidates = isDoctor
            ? await ResolveOwnDoctorAsync(query, cancellationToken)
            : await ResolveRequestedDoctorsAsync(query, cancellationToken);
        if (candidates.IsFailure) return candidates.Error;
        if (candidates.Value.Count == 0) return Result<IReadOnlyList<AvailableSlotDto>>.Success([]);

        var options = schedulingOptions.Value;
        var now = clock.UtcNow;
        var window = isDoctor
            ? BookingWindows.ForDoctor(now, calendar, options)
            : BookingWindows.ForPatient(now, calendar, options);

        return await CalculateSlotsAsync(candidates.Value, window, options, cancellationToken);
    }

    private bool IsAuthenticatedIn(UserRole role) => currentUser.IsAuthenticated && currentUser.IsInRole(role);

    private async Task<Result<IReadOnlyList<int>>> ResolveOwnDoctorAsync(GetAvailableSlotsQuery query, CancellationToken cancellationToken)
    {
        var ownId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (ownId is null) return DoctorErrors.NotFound;
        if (query.DoctorId is not null && query.DoctorId != ownId) return AvailableSlotsErrors.OtherDoctorSchedule;

        return Result<IReadOnlyList<int>>.Success([ownId.Value]);
    }

    private async Task<Result<IReadOnlyList<int>>> ResolveRequestedDoctorsAsync(GetAvailableSlotsQuery query, CancellationToken cancellationToken)
    {
        if (query.DoctorId is not null) return await ResolveRequestedDoctorAsync(query.DoctorId.Value, cancellationToken);
        if (query.SpecialtyId is null) return AvailableSlotsErrors.DoctorOrSpecialtyRequired;

        var doctorIds = await doctorReader.ListActiveDoctorIdsBySpecialtyAsync(
            query.SpecialtyId.Value, query.ProvinceId, query.MunicipalityId, cancellationToken);

        return Result<IReadOnlyList<int>>.Success(doctorIds);
    }

    private async Task<Result<IReadOnlyList<int>>> ResolveRequestedDoctorAsync(int requestedId, CancellationToken cancellationToken)
    {
        var doctorId = await doctorReader.FindActiveDoctorIdAsync(requestedId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        return Result<IReadOnlyList<int>>.Success([doctorId.Value]);
    }

    private async Task<Result<IReadOnlyList<AvailableSlotDto>>> CalculateSlotsAsync(
        IReadOnlyList<int> doctorIds, TimeRange window, SchedulingOptions options, CancellationToken cancellationToken)
    {
        var blocks = await availabilityRepository.ListByDoctorsAsync(doctorIds, window.Start, window.End, cancellationToken);
        var openBlocksByDoctor = blocks
            .Where(block => block.IsAvailable)
            .ToLookup(block => block.DoctorId, block => TimeRange.Create(block.StartsAt, block.EndsAt).Value);
        var appointmentsByDoctor = await appointmentReader.ListActiveIntervalsAsync(
            doctorIds, window.Start, window.End, cancellationToken);

        // La unión sin repetir evita ofrecer dos veces una hora que varios médicos tienen libre.
        var starts = new SortedSet<DateTimeOffset>();
        foreach (var doctorId in doctorIds)
        {
            var appointments = appointmentsByDoctor.GetValueOrDefault(doctorId) ?? [];
            starts.UnionWith(FreeSlotCalculator.Calculate(
                openBlocksByDoctor[doctorId], appointments, window, options.AppointmentDurationHours));
        }

        var duration = TimeSpan.FromHours(options.AppointmentDurationHours);
        IReadOnlyList<AvailableSlotDto> slots = starts
            .Select(start => new AvailableSlotDto(start, start + duration))
            .ToList();

        return Result<IReadOnlyList<AvailableSlotDto>>.Success(slots);
    }
}
