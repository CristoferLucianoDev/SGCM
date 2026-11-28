using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;

/// <summary>
/// El médico autenticado desactiva una plantilla propia: deja de ofrecerse, pero los recordatorios ya programados con
/// ella se envían igual.
/// </summary>
/// <param name="Id">Plantilla a desactivar.</param>
public sealed record DeactivateReminderTemplateCommand(int Id) : ICommand<Unit>;
