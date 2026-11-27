using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>
/// Repositorio del registro clínico. Es de solo inserción: agrega versiones y lee la vigente, pero no actualiza ni
/// borra ninguna, y la base tampoco lo permite.
/// </summary>
public interface IMedicalRecordRepository : IRepository<MedicalRecord>
{
    /// <summary>
    /// Busca la versión vigente del registro de una cita: la de creación más reciente y, si coinciden, la de mayor id,
    /// igual que <see cref="MedicalRecord.Current"/> y la vista <c>clinical.vw_medical_record_current</c>.
    /// </summary>
    /// <param name="appointmentId">Cita cuyo registro se busca.</param>
    /// <param name="cancellationToken">Token de cancelación.</param>
    /// <returns>La versión vigente sin rastrear, o <c>null</c> si la cita no tiene registro.</returns>
    Task<MedicalRecord?> GetCurrentAsync(int appointmentId, CancellationToken cancellationToken);
}
