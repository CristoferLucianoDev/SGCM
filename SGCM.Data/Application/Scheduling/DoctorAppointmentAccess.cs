using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling;

/// <summary>
/// Centraliza cómo un médico obtiene una cita propia, bloqueada para modificarla. La propiedad se comprueba aquí
/// porque el agregado no conoce al usuario autenticado, y una cita ajena responde igual que una inexistente para no
/// revelar citas de otros. El bloqueo evita que dos decisiones simultáneas sobre la misma cita pasen ambas: la
/// segunda la encuentra ya cambiada.
/// </summary>
internal sealed class DoctorAppointmentAccess(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IAppointmentRepository appointmentRepository)
{
    /// <summary>
    /// Busca la cita del médico autenticado y la bloquea con la transacción activa del caso de uso.
    /// </summary>
    /// <param name="appointmentId">Id de la cita.</param>
    /// <param name="notDoctor">Error que se devuelve si el usuario no está autenticado o no es médico.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>
    /// La cita bloqueada, o <paramref name="notDoctor"/>, <see cref="DoctorErrors.NotFound"/> si el médico no tiene
    /// perfil activo, o <see cref="AppointmentErrors.NotFound"/> si la cita no existe o es de otro médico.
    /// </returns>
    public async Task<Result<Appointment>> GetOwnForUpdateAsync(
        int appointmentId, Error notDoctor, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return notDoctor;

        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        var appointment = await appointmentRepository.GetByIdForUpdateAsync(appointmentId, cancellationToken);
        if (appointment is null || appointment.DoctorId != doctorId) return AppointmentErrors.NotFound;

        return appointment;
    }
}
