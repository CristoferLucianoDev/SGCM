using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;

/// <summary>Edita una plantilla propia; la global o la de otro médico se rechazan en <see cref="DoctorReminderTemplateAccess"/>.</summary>
internal sealed class UpdateReminderTemplateCommandHandler(DoctorReminderTemplateAccess access)
    : ICommandHandler<UpdateReminderTemplateCommand, ReminderTemplateDto>
{
    public async Task<Result<ReminderTemplateDto>> HandleAsync(
        UpdateReminderTemplateCommand command, CancellationToken cancellationToken)
    {
        var found = await access.GetOwnForUpdateAsync(command.Id, cancellationToken);
        if (found.IsFailure) return found.Error;

        var template = found.Value;
        var updated = template.Update(command.Name, command.Subject, command.BodyTemplate);
        if (updated.IsFailure) return updated.Error;

        return ReminderTemplateDto.From(template);
    }
}
