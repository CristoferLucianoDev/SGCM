using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;

/// <summary>
/// Desactiva una plantilla propia; la global o la de otro médico se rechazan en <see cref="DoctorReminderTemplateAccess"/>,
/// y una ya inactiva responde ReminderTemplate.AlreadyInactive.
/// </summary>
internal sealed class DeactivateReminderTemplateCommandHandler(DoctorReminderTemplateAccess access)
    : ICommandHandler<DeactivateReminderTemplateCommand, Unit>
{
    public async Task<Result<Unit>> HandleAsync(DeactivateReminderTemplateCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(command.Id, cancellationToken);
        if (found.IsFailure) return found.Error;

        var deactivated = found.Value.Deactivate();
        return deactivated.IsFailure ? deactivated.Error : Unit.Value;
    }
}
