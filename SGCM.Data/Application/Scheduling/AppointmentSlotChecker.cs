using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Comprueba que el horario actual de una cita que se acaba de mover en memoria esté libre en los bloques abiertos
/// de su médico. Lo usan la reprogramación del paciente y la del médico, para no repetir la lectura de bloques y
/// citas ni el bloqueo.
/// </summary>
internal sealed class AppointmentSlotChecker(
    IDoctorAvailabilityRepository availabilityRepository,
    IActiveAppointmentReader appointmentReader)
{
    // La ventana abarca una sola hora para que el único inicio posible sea el de la cita.
    private const int StartWindowHours = 1;

    /// <summary>
    /// Bloquea el bloque abierto del médico y comprueba que el horario actual de la cita (<c>StartsAt</c> y
    /// <c>EndsAt</c>) esté libre, sin contar la propia cita. Debe llamarse dentro de la transacción del caso de uso: el
    /// bloqueo dura hasta su fin y ordena esta comprobación con las reservas simultáneas.
    /// </summary>
    /// <param name="appointment">Cita ya movida en memoria a su nuevo horario.</param>
    /// <param name="durationHours">Duración de la cita en horas, según la configuración.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// <c>true</c> si el horario cabe en un bloque abierto del médico y ninguna otra cita activa lo ocupa; <c>false</c>
    /// si no hay bloque abierto que lo contenga o está ocupado.
    /// </returns>
    public async Task<bool> IsFreeForAsync(Appointment appointment, int durationHours, CancellationToken cancellationToken)
    {
        var (doctorId, startsAt, endsAt) = (appointment.DoctorId, appointment.StartsAt, appointment.EndsAt);
        if (!await availabilityRepository.LockOpenBlockAsync(doctorId, startsAt, endsAt, cancellationToken)) return false;

        var blocks = await availabilityRepository.ListByDoctorAsync(doctorId, startsAt, endsAt, cancellationToken);
        var openBlocks = blocks
            .Where(block => block.IsAvailable)
            .Select(block => TimeRange.Create(block.StartsAt, block.EndsAt).Value)
            .ToList();
        var booked = await appointmentReader.ListActiveIntervalsAsync([doctorId], startsAt, endsAt, cancellationToken);
        IReadOnlyList<BookedInterval> activeAppointments = booked.GetValueOrDefault(doctorId) ?? [];
        var startWindow = TimeRange.Create(startsAt, startsAt.AddHours(StartWindowHours)).Value;

        return FreeSlotCalculator
            .Calculate(openBlocks, activeAppointments, startWindow, durationHours, appointment.Id)
            .Contains(startsAt);
    }
}
