using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;

namespace SGCM.Data.Application.Messaging.ReminderTemplates;

/// <summary>
/// Reglas de acceso compartidas por los comandos de plantillas: solo un médico activo gestiona plantillas, y solo las
/// suyas. Una plantilla de otro médico responde como inexistente para no revelarla; una global, que todos ven, se
/// rechaza por ser de solo lectura.
/// </summary>
internal sealed class DoctorReminderTemplateAccess(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IReminderTemplateRepository templates)
{
    /// <returns>El id del médico autenticado, <see cref="ReminderTemplateErrors.DoctorOnly"/> o <see cref="DoctorErrors.NotFound"/>.</returns>
    public async Task<Result<int>> GetDoctorIdAsync(CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return ReminderTemplateErrors.DoctorOnly;

        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        return doctorId is null ? DoctorErrors.NotFound : doctorId.Value;
    }

    /// <returns>
    /// La plantilla propia con seguimiento; o el error del médico, <see cref="ReminderTemplateErrors.NotFound"/> si no
    /// existe o es de otro médico, o <see cref="ReminderTemplateErrors.GlobalReadOnly"/> si es global.
    /// </returns>
    public async Task<Result<ReminderTemplate>> GetOwnForUpdateAsync(int templateId, CancellationToken cancellationToken)
    {
        var doctorId = await GetDoctorIdAsync(cancellationToken);
        if (doctorId.IsFailure) return doctorId.Error;

        var template = await templates.GetByIdForUpdateAsync(templateId, cancellationToken);
        if (template is null) return ReminderTemplateErrors.NotFound;
        if (template.IsGlobal) return ReminderTemplateErrors.GlobalReadOnly;
        if (!template.IsOwnedBy(doctorId.Value)) return ReminderTemplateErrors.NotFound;

        return template;
    }
}
