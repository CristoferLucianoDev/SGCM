using SGCM.Data.Application.Common;
using SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;
using SGCM.Data.Application.Messaging.Queries.ListReminderTemplates;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Web.Contracts.Messaging;

namespace SGCM.Web.Mappers.Messaging;

/// <summary>Convierte los contratos de la API de plantillas en comandos, y sus resultados en respuestas.</summary>
public static class ReminderTemplateMapper
{
    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static CreateReminderTemplateCommand ToCreateCommand(this ReminderTemplateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new CreateReminderTemplateCommand(request.Name, request.Subject, request.BodyTemplate);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static UpdateReminderTemplateCommand ToUpdateCommand(this ReminderTemplateRequest request, int id)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new UpdateReminderTemplateCommand(id, request.Name, request.Subject, request.BodyTemplate);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="request"/> es nulo.</exception>
    public static ListReminderTemplatesQuery ToQuery(this ListReminderTemplatesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new ListReminderTemplatesQuery(request.PageNumber, request.PageSize);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="template"/> es nulo.</exception>
    public static ReminderTemplateResponse ToResponse(this ReminderTemplateDto template)
    {
        ArgumentNullException.ThrowIfNull(template);

        return new ReminderTemplateResponse(
            template.Id, template.Name, template.Subject, template.BodyTemplate, template.IsActive, template.IsReadOnly);
    }

    /// <exception cref="ArgumentNullException">Si <paramref name="page"/> es nulo.</exception>
    public static ListReminderTemplatesResponse ToResponse(this PagedResult<ReminderTemplateDto> page)
    {
        ArgumentNullException.ThrowIfNull(page);

        return new ListReminderTemplatesResponse(
            page.Items.Select(template => template.ToResponse()).ToArray(), page.PageNumber, page.PageSize, page.TotalCount);
    }
}
