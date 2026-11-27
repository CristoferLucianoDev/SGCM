using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.ExtendAppointment;

/// <summary>
/// Extiende la cita en curso del médico autenticado. La cita propia y bloqueada la entrega
/// <see cref="DoctorAppointmentAccess"/>. Lee los bloques desde el inicio de la cita hasta el nuevo fin, porque la
/// base exige que la cita completa quepa en un solo bloque, y las citas activas solo del tramo nuevo. Un choque
/// simultáneo con otra reserva lo rechaza la base (ex_appointment_doctor_no_overlap) y llega como
/// <see cref="AppointmentErrors.SlotUnavailable"/>.
/// </summary>
internal sealed class ExtendAppointmentCommandHandler(
    DoctorAppointmentAccess access,
    IDoctorAvailabilityRepository availabilityRepository,
    IActiveAppointmentReader appointmentReader,
    IDateTimeProvider clock) : ICommandHandler<ExtendAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(ExtendAppointmentCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.ProgressDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var appointment = found.Value;
        var newEnd = appointment.EndsAt.AddHours(command.AdditionalHours);
        var openBlocks = await ReadOpenBlocksAsync(appointment, newEnd, cancellationToken);
        var otherAppointments = await ReadOtherAppointmentsAsync(appointment, newEnd, cancellationToken);

        var extended = appointment.Extend(command.AdditionalHours, clock.UtcNow, openBlocks, otherAppointments);
        if (extended.IsFailure) return extended.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }

    private async Task<IReadOnlyCollection<TimeRange>> ReadOpenBlocksAsync(
        Appointment appointment, DateTimeOffset newEnd, CancellationToken cancellationToken)
    {
        var blocks = await availabilityRepository.ListByDoctorAsync(
            appointment.DoctorId, appointment.StartsAt, newEnd, cancellationToken);

        // Los bloques guardados ya son válidos, así que Create no falla.
        return blocks
            .Where(block => block.IsAvailable)
            .Select(block => TimeRange.Create(block.StartsAt, block.EndsAt).Value)
            .ToList();
    }

    private async Task<IReadOnlyCollection<TimeRange>> ReadOtherAppointmentsAsync(
        Appointment appointment, DateTimeOffset newEnd, CancellationToken cancellationToken)
    {
        var byDoctor = await appointmentReader.ListActiveIntervalsAsync(
            [appointment.DoctorId], appointment.EndsAt, newEnd, cancellationToken);
        IReadOnlyList<BookedInterval> booked = byDoctor.GetValueOrDefault(appointment.DoctorId) ?? [];

        // La propia cita también es activa: se excluye para que nunca se bloquee a sí misma al extenderse.
        return booked
            .Where(interval => interval.AppointmentId != appointment.Id)
            .Select(interval => interval.Range)
            .ToList();
    }
}
