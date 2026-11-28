using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using SGCM.Web.Contracts.Clinical;

namespace SGCM.Web.Mappers.Clinical;

/// <summary>
/// Convierte los contratos de la API del registro clínico y del historial médico en comandos, y sus resultados en
/// respuestas.
/// </summary>
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

    /// <exception cref="ArgumentNullException">Si <paramref name="history"/> es nulo.</exception>
    public static MedicalHistoryResponse ToResponse(this MedicalHistoryDto history)
    {
        ArgumentNullException.ThrowIfNull(history);

        return new MedicalHistoryResponse(history.PatientId, history.Entries.Select(ToResponse).ToList());
    }

    private static MedicalHistoryEntryResponse ToResponse(MedicalHistoryEntryDto entry) =>
        new(
            entry.AppointmentId,
            entry.StartsAt,
            entry.EndsAt,
            // Mismo texto que el resto de la API: el nombre del estado en minúsculas (confirmed, completed...).
            entry.Status.ToString().ToLowerInvariant(),
            entry.DoctorId,
            entry.DoctorName,
            entry.SpecialtyName,
            entry.HasMedicalRecord,
            entry.CurrentVersion is null ? null : ToResponse(entry.CurrentVersion),
            entry.PreviousVersions.Select(ToResponse).ToList());

    private static MedicalHistoryVersionResponse ToResponse(MedicalRecordVersionDto version) =>
        new(version.Id, version.Diagnosis, version.Treatment, version.Notes, version.CreatedAt);
}
