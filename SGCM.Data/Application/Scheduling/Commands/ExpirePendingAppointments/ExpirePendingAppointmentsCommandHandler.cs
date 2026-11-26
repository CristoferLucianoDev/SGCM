using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;

/// <summary>
/// Es idempotente porque <see cref="Appointment.Expire"/> rechaza las citas que ya no están pendientes o que
/// todavía están en plazo. Cada cita se bloquea para no chocar con una confirmación o cancelación simultánea; el
/// decorador guarda todo en una transacción y los eventos se publican después del commit. Procesa un lote por
/// ejecución, las más antiguas primero.
/// </summary>
internal sealed class ExpirePendingAppointmentsCommandHandler(
    IAppointmentRepository appointmentRepository,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<ExpirePendingAppointmentsCommand, int>
{
    // Acota la transacción y los bloqueos; como Expire es idempotente, la siguiente ejecución toma las que falten.
    private const int BatchSize = 100;

    public async Task<Result<int>> HandleAsync(
        ExpirePendingAppointmentsCommand command, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var hours = schedulingOptions.Value.ConfirmationDeadlineHours;
        var candidateIds = await appointmentRepository.ListExpirableIdsAsync(now, hours, BatchSize, cancellationToken);

        var expired = 0;
        foreach (var id in candidateIds)
        {
            var appointment = await appointmentRepository.GetByIdForUpdateAsync(id, cancellationToken);
            // Si la cita ya no existe o Expire la rechaza (ya decidida o aún en plazo), no se corta el lote.
            if (appointment?.Expire(now, hours).IsSuccess == true) expired++;
        }

        return expired;
    }
}
