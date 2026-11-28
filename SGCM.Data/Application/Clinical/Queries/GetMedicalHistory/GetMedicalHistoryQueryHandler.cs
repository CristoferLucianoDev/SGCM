using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Clinical.MedicalHistory;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;

/// <summary>
/// Resuelve el historial médico de un paciente. Quién lo consulta y qué ve lo decide
/// <see cref="MedicalHistoryAccessPolicy"/>: el paciente ve la versión vigente de cada cita, y el médico tratante ve
/// además las versiones anteriores de las citas que atendió. Cada versión mostrada deja una fila de consulta (view)
/// en el registro de accesos.
/// </summary>
internal sealed class GetMedicalHistoryQueryHandler(
    MedicalHistoryAccessPolicy policy,
    IMedicalHistoryReader reader,
    IMedicalRecordAccessLogRepository accessLog,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock,
    IValidator<GetMedicalHistoryQuery> validator) : IQueryHandler<GetMedicalHistoryQuery, MedicalHistoryDto>
{
    public async Task<Result<MedicalHistoryDto>> HandleAsync(
        GetMedicalHistoryQuery query, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        var granted = query.PatientId is { } patientId
            ? await policy.ForCurrentDoctorAsync(patientId, cancellationToken)
            : await policy.ForCurrentPatientAsync(cancellationToken);
        if (granted.IsFailure) return granted.Error;

        var access = granted.Value;
        var appointments = await reader.ListAsync(access.PatientId, cancellationToken);
        var entries = appointments.Select(appointment => ToEntry(appointment, access)).ToList();

        await LogViewsAsync(entries, query.IpAddress, cancellationToken);

        return new MedicalHistoryDto(access.PatientId, entries);
    }

    private static MedicalHistoryEntryDto ToEntry(MedicalHistoryAppointmentData appointment, MedicalHistoryAccess access)
    {
        var versions = appointment.Versions.Select(ToVersion).ToList();
        List<MedicalRecordVersionDto> previous = access.ShowsPreviousVersions(appointment.DoctorId)
            ? versions.Skip(1).ToList()
            : [];

        return new MedicalHistoryEntryDto(
            appointment.AppointmentId, appointment.StartsAt, appointment.EndsAt, appointment.Status,
            appointment.DoctorId, appointment.DoctorName, appointment.SpecialtyName,
            versions.FirstOrDefault(), previous);
    }

    private static MedicalRecordVersionDto ToVersion(MedicalHistoryVersionData version) =>
        new(version.Id, version.Diagnosis, version.Treatment, version.Notes, version.CreatedAt);

    // Cada versión mostrada, vigente o anterior, es un acceso. Una consulta sin registros no deja filas: la tabla exige
    // la versión accedida. Esta consulta no pasa por el decorador de transacción, así que guarda aquí.
    private async Task LogViewsAsync(
        IReadOnlyList<MedicalHistoryEntryDto> entries, string? ipAddress, CancellationToken cancellationToken)
    {
        var shown = entries
            .Where(entry => entry.CurrentVersion is not null)
            .SelectMany(entry => entry.PreviousVersions.Prepend(entry.CurrentVersion!))
            .ToList();
        if (shown.Count == 0) return;

        var accessedAt = clock.UtcNow;
        foreach (var version in shown)
        {
            accessLog.Add(MedicalRecordAccessLog.Record(
                version.Id, currentUser.UserId, MedicalRecordAccessType.View, accessedAt, ipAddress));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
