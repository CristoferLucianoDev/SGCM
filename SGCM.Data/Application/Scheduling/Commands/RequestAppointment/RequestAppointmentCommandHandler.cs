using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Availability;

namespace SGCM.Data.Application.Scheduling.Commands.RequestAppointment;

/// <summary>
/// Crea la cita pendiente. Antes de crearla bloquea el bloque de disponibilidad del médico y vuelve a comprobar
/// que el horario siga libre, para que dos solicitudes del mismo horario no pasen a la vez. Si el médico lo eligió
/// el paciente y perdió el horario, se rechaza; si lo asignó el sistema, se asigna otro médico libre y se informa
/// el cambio, y solo se rechaza si no queda ninguno. Si aun así dos citas chocan, las restricciones de la base
/// lo convierten en conflicto.
/// </summary>
internal sealed class RequestAppointmentCommandHandler(
    ICurrentUser currentUser,
    IPatientRepository patientRepository,
    IActiveDoctorReader doctorReader,
    IDoctorAssignmentService assignmentService,
    IDoctorAvailabilityRepository availabilityRepository,
    IActiveAppointmentReader appointmentReader,
    IDoctorProfileReader doctorProfileReader,
    IAppointmentRepository appointmentRepository,
    IUnitOfWork unitOfWork,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions) : ICommandHandler<RequestAppointmentCommand, RequestedAppointmentDto>
{
    public async Task<Result<RequestedAppointmentDto>> HandleAsync(
        RequestAppointmentCommand command, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Patient)) return AppointmentErrors.PatientOnly;

        var patient = await patientRepository.GetByUserIdAsync(currentUser.UserId, cancellationToken);
        if (patient is null) return PatientErrors.ProfileNotFound;

        var options = schedulingOptions.Value;
        var window = BookingWindows.ForPatient(clock.UtcNow, calendar, options);
        var start = Appointment.ValidateStart(command.StartsAt, window);
        if (start != Error.None) return start;

        var slot = TimeRange.Create(command.StartsAt, command.StartsAt.AddHours(options.AppointmentDurationHours)).Value;
        var doctor = command.DoctorId is { } chosenDoctorId
            ? await HoldChosenDoctorAsync(command.SpecialtyId, chosenDoctorId, slot, cancellationToken)
            : await HoldAssignedDoctorAsync(command, slot, cancellationToken);
        if (doctor.IsFailure) return doctor.Error;

        var created = Appointment.Request(
            patient.Id, doctor.Value.DoctorId, command.SpecialtyId, command.StartsAt, command.ReasonForVisit,
            options.AppointmentDurationHours, window);
        if (created.IsFailure) return created.Error;
        var appointment = created.Value;

        appointmentRepository.Add(appointment);
        // El ID se genera al guardar; el decorador confirma la transacción.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // El médico sigue activo: su bloque está bloqueado y la base no permite citas con médicos inactivos.
        var profile = await doctorProfileReader.GetPublicAsync(appointment.DoctorId, cancellationToken);
        if (profile is null) return AppointmentErrors.SlotUnavailable;

        return new RequestedAppointmentDto(
            appointment.Id, appointment.DoctorId, profile.FullName, profile.ConsultationFee,
            appointment.StartsAt, appointment.EndsAt, doctor.Value.Changed);
    }

    private async Task<Result<HeldDoctor>> HoldChosenDoctorAsync(
        int specialtyId, int doctorId, TimeRange slot, CancellationToken cancellationToken)
    {
        var doctors = await doctorReader.ListActiveDoctorIdsBySpecialtyAsync(specialtyId, null, null, cancellationToken);
        if (!doctors.Contains(doctorId)) return AppointmentErrors.DoctorNotInSpecialty;

        return await TryHoldAsync(doctorId, slot, cancellationToken)
            ? new HeldDoctor(doctorId, Changed: false)
            : AppointmentErrors.SlotUnavailable;
    }

    /// <summary>
    /// Intenta conservar el médico asignado en el resumen; si no hay o ya no tiene el horario, asigna otro hasta
    /// que uno lo tenga libre bajo el bloqueo o no quede ninguno.
    /// </summary>
    private async Task<Result<HeldDoctor>> HoldAssignedDoctorAsync(
        RequestAppointmentCommand command, TimeRange slot, CancellationToken cancellationToken)
    {
        var tried = new HashSet<int>();
        var candidate = await EligibleAssignedDoctorAsync(command, cancellationToken);

        while (true)
        {
            if (candidate is null)
            {
                var assigned = await assignmentService.AssignAsync(
                    command.SpecialtyId, slot.Start, command.ProvinceId, command.MunicipalityId, cancellationToken);
                if (assigned.IsFailure) return assigned.Error;
                candidate = assigned.Value;
            }

            // Bajo el bloqueo, un médico que perdió el horario ya no lo tiene libre al volver a asignar; si el
            // servicio lo devolviera otra vez, se corta para no repetir el intento.
            if (!tried.Add(candidate.Value)) return AppointmentErrors.SlotUnavailable;

            if (await TryHoldAsync(candidate.Value, slot, cancellationToken))
            {
                var changed = command.AssignedDoctorId is not null && candidate.Value != command.AssignedDoctorId;
                return new HeldDoctor(candidate.Value, changed);
            }

            candidate = null;
        }
    }

    /// <summary>El médico asignado en el resumen, solo si sigue siendo de la especialidad y de la ubicación.</summary>
    private async Task<int?> EligibleAssignedDoctorAsync(RequestAppointmentCommand command, CancellationToken cancellationToken)
    {
        if (command.AssignedDoctorId is not { } assignedDoctorId) return null;

        var eligible = await doctorReader.ListActiveDoctorIdsBySpecialtyAsync(
            command.SpecialtyId, command.ProvinceId, command.MunicipalityId, cancellationToken);

        return eligible.Contains(assignedDoctorId) ? assignedDoctorId : null;
    }

    /// <summary>Bloquea el bloque abierto que contiene el horario y comprueba que el médico no tenga citas en él.</summary>
    private async Task<bool> TryHoldAsync(int doctorId, TimeRange slot, CancellationToken cancellationToken)
    {
        if (!await availabilityRepository.LockOpenBlockAsync(doctorId, slot.Start, slot.End, cancellationToken)) return false;

        var booked = await appointmentReader.ListActiveIntervalsAsync([doctorId], slot.Start, slot.End, cancellationToken);
        return !booked.ContainsKey(doctorId);
    }

    private sealed record HeldDoctor(int DoctorId, bool Changed);
}
