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
/// Calcula los horarios libres de un médico. La ventana depende de quién consulta: el paciente ve desde la
/// antelación mínima hasta el horizonte de reservas y el médico ve los suyos desde el instante actual. Las dos
/// lecturas de la base son secuenciales porque comparten el mismo contexto.
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

        var doctor = isDoctor
            ? await ResolveOwnDoctorAsync(query, cancellationToken)
            : await ResolveRequestedDoctorAsync(query, cancellationToken);
        if (doctor.IsFailure) return doctor.Error;

        var options = schedulingOptions.Value;
        var now = clock.UtcNow;
        var window = isDoctor
            ? BookingWindows.ForDoctor(now, calendar, options)
            : BookingWindows.ForPatient(now, calendar, options);

        return await CalculateSlotsAsync(doctor.Value, window, options, cancellationToken);
    }

    private bool IsAuthenticatedIn(UserRole role) => currentUser.IsAuthenticated && currentUser.IsInRole(role);

    private async Task<Result<int>> ResolveOwnDoctorAsync(GetAvailableSlotsQuery query, CancellationToken cancellationToken)
    {
        var ownId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (ownId is null) return DoctorErrors.NotFound;
        if (query.DoctorId is not null && query.DoctorId != ownId) return AvailableSlotsErrors.OtherDoctorSchedule;

        return ownId.Value;
    }

    private async Task<Result<int>> ResolveRequestedDoctorAsync(GetAvailableSlotsQuery query, CancellationToken cancellationToken)
    {
        if (query.DoctorId is null) return AvailableSlotsErrors.DoctorRequired;

        var doctorId = await doctorReader.FindActiveDoctorIdAsync(query.DoctorId.Value, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        return doctorId.Value;
    }

    private async Task<Result<IReadOnlyList<AvailableSlotDto>>> CalculateSlotsAsync(
        int doctorId, TimeRange window, SchedulingOptions options, CancellationToken cancellationToken)
    {
        var blocks = await availabilityRepository.ListByDoctorAsync(doctorId, window.Start, window.End, cancellationToken);
        var openBlocks = blocks
            .Where(block => block.IsAvailable)
            .Select(block => TimeRange.Create(block.StartsAt, block.EndsAt).Value)
            .ToList();
        var appointments = await appointmentReader.ListActiveIntervalsAsync(
            doctorId, window.Start, window.End, cancellationToken);

        var duration = TimeSpan.FromHours(options.AppointmentDurationHours);
        IReadOnlyList<AvailableSlotDto> slots = FreeSlotCalculator
            .Calculate(openBlocks, appointments, window, options.AppointmentDurationHours)
            .Select(start => new AvailableSlotDto(start, start + duration))
            .ToList();

        return Result<IReadOnlyList<AvailableSlotDto>>.Success(slots);
    }
}
