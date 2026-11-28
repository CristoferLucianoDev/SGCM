using FluentValidation;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;

/// <summary>Valida el id; los datos de la plantilla los valida el agregado.</summary>
public sealed class UpdateReminderTemplateCommandValidator : AbstractValidator<UpdateReminderTemplateCommand>
{
    public UpdateReminderTemplateCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithErrorCode(ReminderTemplateErrors.InvalidId.Code)
            .WithMessage(ReminderTemplateErrors.InvalidId.Description);
    }
}
