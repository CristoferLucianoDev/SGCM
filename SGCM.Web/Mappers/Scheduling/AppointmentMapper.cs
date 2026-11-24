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
}
