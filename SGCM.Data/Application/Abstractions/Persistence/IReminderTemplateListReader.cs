using SGCM.Data.Application.Common;
using SGCM.Data.Application.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Abstractions.Persistence;

/// <summary>Lectura paginada de las plantillas que un médico puede usar.</summary>
public interface IReminderTemplateListReader
{
    /// <summary>
    /// Las plantillas activas del médico y las globales activas, marcadas como de solo lectura. Primero las globales y
    /// después las propias, cada grupo por nombre.
    /// </summary>
    Task<PagedResult<ReminderTemplateDto>> ListAsync(
        int doctorId, int pageNumber, int pageSize, CancellationToken cancellationToken);
}
