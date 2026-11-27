using SGCM.Data.Domain.Clinical.MedicalRecords;

namespace SGCM.Data.Application.Clinical;

/// <summary>
/// Accesos al registro clínico que hizo el comando en curso. El handler informa cada versión que consulta, crea o
/// corrige, y el decorador del registro de accesos los guarda cuando el comando termina bien. Vive una solicitud.
/// </summary>
internal sealed class MedicalRecordAccessTracker
{
    private readonly List<MedicalRecordAccess> _accesses = [];

    /// <summary>Accesos informados, en el orden en que ocurrieron.</summary>
    public IReadOnlyList<MedicalRecordAccess> Accesses => _accesses.AsReadOnly();

    /// <summary>Informa un acceso a una versión. Puede estar aún sin guardar: su id se lee al registrar el acceso.</summary>
    /// <param name="record">Versión consultada, creada o corregida.</param>
    /// <param name="accessType">Tipo de acceso.</param>
    public void Track(MedicalRecord record, MedicalRecordAccessType accessType)
    {
        ArgumentNullException.ThrowIfNull(record);
        _accesses.Add(new MedicalRecordAccess(record, accessType));
    }

    /// <summary>Descarta los accesos ya registrados.</summary>
    public void Clear() => _accesses.Clear();
}

/// <summary>Un acceso informado al <see cref="MedicalRecordAccessTracker"/>.</summary>
/// <param name="Record">Versión a la que se accedió.</param>
/// <param name="AccessType">Tipo de acceso.</param>
internal sealed record MedicalRecordAccess(MedicalRecord Record, MedicalRecordAccessType AccessType);
