using FluentValidation;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;

/// <summary>Valida que el id sea mayor que cero.</summary>
public sealed class DeactivateReminderTemplateCommandValidator : AbstractValidator<DeactivateReminderTemplateCommand>
{
    public DeactivateReminderTemplateCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0)
            .WithErrorCode(ReminderTemplateErrors.InvalidId.Code)
            .WithMessage(ReminderTemplateErrors.InvalidId.Description);
    }
}
