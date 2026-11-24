using SGCM.Data.Application.Scheduling.Commands.RequestAppointment;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorAssignment;
using SGCM.Web.Contracts.Scheduling;

namespace SGCM.Web.Mappers.Scheduling;

/// <summary>Convierte los contratos de la API de citas en consultas y comandos, y sus resultados en respuestas.</summary>
public static class AppointmentMapper
{
    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static GetDoctorAssignmentQuery ToQuery(this DoctorAssignmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new GetDoctorAssignmentQuery(request.SpecialtyId, request.StartsAt, request.ProvinceId, request.MunicipalityId);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="assignment"/> es nulo.</exception>
    public static DoctorAssignmentResponse ToResponse(this DoctorAssignmentDto assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new DoctorAssignmentResponse(assignment.DoctorId, assignment.DoctorName, assignment.ConsultationFee);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static RequestAppointmentCommand ToCommand(this RequestAppointmentRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new RequestAppointmentCommand(
            request.SpecialtyId,
            request.StartsAt,
            request.DoctorId,
            request.AssignedDoctorId,
            request.ProvinceId,
            request.MunicipalityId,
            request.ReasonForVisit);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="appointment"/> es nulo.</exception>
    public static RequestAppointmentResponse ToResponse(this RequestedAppointmentDto appointment)
    {
        ArgumentNullException.ThrowIfNull(appointment);

        return new RequestAppointmentResponse(
            appointment.AppointmentId,
            appointment.DoctorId,
            appointment.DoctorName,
            appointment.ConsultationFee,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.DoctorChanged);
    }
}
