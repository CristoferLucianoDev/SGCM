using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Commands.CancelAppointmentByDoctor;

/// <summary>
/// Cancela la cita del médico autenticado. La propiedad se comprueba aquí porque el agregado no conoce al
/// usuario autenticado, y una cita ajena responde igual que una inexistente para no revelar citas de otros.
/// La cita se bloquea para que dos cancelaciones simultáneas no pasen ambas: la segunda la encuentra
/// cancelada y recibe <see cref="AppointmentErrors.InvalidTransition"/>.
/// </summary>
internal sealed class CancelAppointmentByDoctorCommandHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IAppointmentRepository appointmentRepository,
    IDateTimeProvider clock,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<CancelAppointmentByDoctorCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(
        CancelAppointmentByDoctorCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor))
            return AppointmentErrors.CancellationDoctorOnly;

        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        var appointment = await appointmentRepository.GetByIdForUpdateAsync(command.AppointmentId, cancellationToken);
        if (appointment is null || appointment.DoctorId != doctorId) return AppointmentErrors.NotFound;

        var options = schedulingOptions.Value;
        var cancelled = appointment.Cancel(
            AppointmentCanceller.Doctor,
            command.Reason,
            clock.UtcNow,
            options.PatientCancellationDeadlineHours,
            options.ToShortNoticeCancellation());
        if (cancelled.IsFailure) return cancelled.Error;

        // El TransactionDecorator guarda los cambios y confirma la transacción.
        return Unit.Value;
    }
}
