using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CompleteFinishedAppointments;

/// <summary>
/// Es idempotente porque <see cref="Appointment.CompleteFinished"/> rechaza las citas que ya no están confirmadas o
/// que todavía no terminan. Cada cita se bloquea para no chocar con una extensión o un cierre del médico a la vez: si
/// la extendió, se relee el fin nuevo y se salta. El decorador guarda todo en una transacción y los eventos se
/// publican después del commit. Procesa un lote por ejecución, las más antiguas primero.
/// </summary>
internal sealed class CompleteFinishedAppointmentsCommandHandler(
    IAppointmentRepository appointmentRepository,
    IDateTimeProvider clock) : ICommandHandler<CompleteFinishedAppointmentsCommand, int>
{
    // Acota la transacción y los bloqueos; como CompleteFinished es idempotente,
    // la siguiente ejecución toma las que falten.
    private const int BatchSize = 100;

    public async Task<Result<int>> HandleAsync(
        CompleteFinishedAppointmentsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var candidateIds = await appointmentRepository.ListFinishedIdsAsync(now, BatchSize, cancellationToken);

        var completed = 0;
        foreach (var id in candidateIds)
        {
            var appointment = await appointmentRepository.GetByIdForUpdateAsync(id, cancellationToken);
            // Si la cita ya no existe o CompleteFinished la rechaza (ya cerrada o extendida), no se corta el lote.
            if (appointment?.CompleteFinished(now).IsSuccess == true) completed++;
        }

        return completed;
    }
}
