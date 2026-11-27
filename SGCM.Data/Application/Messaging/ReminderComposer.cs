using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging;

/// <summary>
/// Con una plantilla y los datos de la cita produce el asunto y el texto final del recordatorio. Lo usan el
/// recordatorio automático y los de seguimiento. El texto queda sin codificar como HTML: es lo que se guarda y lo que
/// el médico edita; el correo lo codifica al enviarlo.
/// </summary>
internal sealed class ReminderComposer(ITemplateRenderer renderer)
{
    public ComposedReminder Compose(ReminderTemplate template, AppointmentNotificationData appointment)
    {
        ArgumentNullException.ThrowIfNull(template);

        var values = AppointmentTemplateValues.From(appointment);

        return new ComposedReminder(
            renderer.Render(template.Subject, values, htmlEncode: false),
            renderer.Render(template.BodyTemplate, values, htmlEncode: false));
    }
}
