using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CompleteAppointment;

/// <summary>
/// Termina la cita en curso del médico autenticado. La cita propia y bloqueada la entrega
/// <see cref="DoctorAppointmentAccess"/>, así que dos terminaciones simultáneas no pasan ambas: la segunda la
/// encuentra ya completada y recibe <see cref="AppointmentErrors.NotInProgress"/>.
/// </summary>
internal sealed class CompleteAppointmentCommandHandler(
    DoctorAppointmentAccess access,
    IDateTimeProvider clock) : ICommandHandler<CompleteAppointmentCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(CompleteAppointmentCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.ProgressDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var completed = found.Value.Complete(clock.UtcNow);
        if (completed.IsFailure) return completed.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
