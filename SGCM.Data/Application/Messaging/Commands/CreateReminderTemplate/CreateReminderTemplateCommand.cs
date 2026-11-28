using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;

/// <summary>El médico autenticado crea una plantilla propia, activa.</summary>
/// <param name="Name">Nombre, hasta 100 caracteres.</param>
/// <param name="Subject">Asunto, hasta 200 caracteres; solo admite las variables permitidas.</param>
/// <param name="BodyTemplate">Cuerpo, hasta 2000 caracteres; solo admite las variables permitidas.</param>
public sealed record CreateReminderTemplateCommand(string? Name, string? Subject, string? BodyTemplate)
    : ICommand<ReminderTemplateDto>;
