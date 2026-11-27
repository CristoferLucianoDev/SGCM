using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Web.Contracts.Clinical;

namespace SGCM.Web.Mappers.Clinical;

/// <summary>Convierte los contratos de la API del registro clínico en comandos, y sus resultados en respuestas.</summary>
public static class MedicalRecordMapper
{
    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static CreateMedicalRecordVersionCommand ToCommand(
        this CreateMedicalRecordVersionRequest request, int appointmentId, string? ipAddress)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new CreateMedicalRecordVersionCommand(
            appointmentId, request.Diagnosis, request.Treatment, request.Notes, ipAddress);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="result"/> es nulo.</exception>
    public static MedicalRecordVersionResponse ToResponse(this MedicalRecordVersionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new MedicalRecordVersionResponse(
            result.Id, result.AppointmentId, result.Diagnosis, result.Treatment, result.Notes, result.CreatedAt,
            result.IsCorrection);
    }
}
