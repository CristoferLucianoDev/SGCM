using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Queries.ListReminderTemplates;

/// <summary>
/// Plantillas que el médico autenticado puede usar: sus plantillas activas y las globales activas, estas marcadas como
/// de solo lectura, en una lista paginada.
/// </summary>
/// <param name="PageNumber">Página, desde 1.</param>
/// <param name="PageSize">Tamaño de página, de 1 a 100.</param>
public sealed record ListReminderTemplatesQuery(int PageNumber = 1, int PageSize = 20)
    : IQuery<PagedResult<ReminderTemplateDto>>;
