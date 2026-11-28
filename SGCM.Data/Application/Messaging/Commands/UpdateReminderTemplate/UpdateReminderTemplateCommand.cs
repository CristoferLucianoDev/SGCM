using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;

/// <summary>
/// El médico autenticado reemplaza nombre, asunto y cuerpo de una plantilla propia. Los recordatorios ya programados o
/// enviados no cambian: guardan su texto final.
/// </summary>
/// <param name="Id">Plantilla a editar.</param>
/// <param name="Name">Nombre, hasta 100 caracteres.</param>
/// <param name="Subject">Asunto, hasta 200 caracteres; solo admite las variables permitidas.</param>
/// <param name="BodyTemplate">Cuerpo, hasta 2000 caracteres; solo admite las variables permitidas.</param>
public sealed record UpdateReminderTemplateCommand(int Id, string? Name, string? Subject, string? BodyTemplate)
    : ICommand<ReminderTemplateDto>;
