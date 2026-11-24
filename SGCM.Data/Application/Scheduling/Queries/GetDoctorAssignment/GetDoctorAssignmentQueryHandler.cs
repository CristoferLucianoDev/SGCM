using FluentValidation;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;

/// <summary>
/// Asigna al azar un médico libre de la especialidad con <see cref="IDoctorAssignmentService"/> y devuelve su
/// nombre y su tarifa. El horario debe estar dentro de la ventana de reservas del paciente, la misma que usa la
/// solicitud de cita; la asignación no reserva nada, así que al enviar la solicitud se vuelve a comprobar.
/// </summary>
internal sealed class GetDoctorAssignmentQueryHandler(
    ICurrentUser currentUser,
    IDoctorAssignmentService assignmentService,
    IDoctorProfileReader doctorProfileReader,
    IDateTimeProvider clock,
    BusinessCalendar calendar,
    IOptions<SchedulingOptions> schedulingOptions,
    IValidator<GetDoctorAssignmentQuery> validator) : IQueryHandler<GetDoctorAssignmentQuery, DoctorAssignmentDto>
{
    public async Task<Result<DoctorAssignmentDto>> HandleAsync(
        GetDoctorAssignmentQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Patient)) return AppointmentErrors.PatientOnly;

        var window = BookingWindows.ForPatient(clock.UtcNow, calendar, schedulingOptions.Value);
        if (query.StartsAt < window.Start) return AppointmentErrors.TooSoon;
        if (query.StartsAt >= window.End) return AppointmentErrors.BeyondHorizon;

        var assignment = await assignmentService.AssignAsync(
            query.SpecialtyId, query.StartsAt, query.ProvinceId, query.MunicipalityId, cancellationToken);
        if (assignment.IsFailure) return assignment.Error;

        // Solo falla si el médico se desactivó entre la asignación y la lectura del perfil.
        var doctor = await doctorProfileReader.GetPublicAsync(assignment.Value, cancellationToken);
        if (doctor is null) return AppointmentErrors.SlotUnavailable;

        return new DoctorAssignmentDto(doctor.Id, doctor.FullName, doctor.ConsultationFee);
    }
}
