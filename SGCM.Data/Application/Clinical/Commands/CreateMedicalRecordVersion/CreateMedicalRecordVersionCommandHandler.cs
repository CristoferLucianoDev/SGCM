using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;

/// <summary>
/// Crea una versión del registro clínico de una cita del médico autenticado. La cita propia y bloqueada la entrega
/// <see cref="DoctorAppointmentAccess"/>: así dos registros simultáneos de la misma cita se ordenan, y el segundo
/// corrige al primero, y la cita no cambia de estado mientras se registra. La creación queda en el registro de
/// accesos por medio de <see cref="MedicalRecordAccessTracker"/>.
/// </summary>
internal sealed class CreateMedicalRecordVersionCommandHandler(
    DoctorAppointmentAccess access,
    IMedicalRecordRepository records,
    MedicalRecordAccessTracker tracker,
    ICurrentUser currentUser,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock) : ICommandHandler<CreateMedicalRecordVersionCommand, MedicalRecordVersionResult>
{
    public async Task<Result<MedicalRecordVersionResult>> HandleAsync(
        CreateMedicalRecordVersionCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(
            command.AppointmentId, AppointmentErrors.ClinicalRecordDoctorOnly, cancellationToken);
        if (found.IsFailure) return found.Error;

        var appointment = found.Value;
        var now = clock.UtcNow;
        // La propiedad ya la comprobó DoctorAppointmentAccess (una cita ajena responde como inexistente); aquí se
        // validan el estado y la hora de inicio.
        var allowed = appointment.EnsureClinicalRecordAllowed(appointment.DoctorId, now);
        if (allowed.IsFailure) return allowed.Error;

        var content = MedicalRecordContent.Create(command.Diagnosis, command.Treatment, command.Notes);
        if (content.IsFailure) return content.Error;

        var current = await records.GetCurrentAsync(appointment.Id, cancellationToken);
        var version = current is null
            ? MedicalRecord.Create(appointment.Id, appointment.PatientId, content.Value, currentUser.UserId, now)
            : current.Correct(content.Value, currentUser.UserId, now);

        records.Add(version);
        tracker.Track(version, current is null ? MedicalRecordAccessType.Create : MedicalRecordAccessType.Update);
        // El id se genera al guardar; el decorador de transacción confirma junto con el registro de accesos.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new MedicalRecordVersionResult(
            version.Id, version.AppointmentId, version.Diagnosis, version.Treatment, version.Notes, version.CreatedAt,
            IsCorrection: current is not null);
    }
}
