using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Plantillas de recordatorio para pruebas unitarias. En este PBI la plantilla solo se lee de la base, así que no
/// tiene fábrica: se arma con reflexión, igual que la materializa EF.
/// </summary>
public static class ReminderTemplateMother
{
    public const int GlobalId = 1;
    public const string GlobalSubject = "Recordatorio: su cita de {SpecialtyName} es manana";
    public const string GlobalBody =
        "Hola {PatientName}, le recordamos su cita de {SpecialtyName} con {DoctorName} el {AppointmentDate} a las {StartTime}.";

    public static ReminderTemplate Global(string subject = GlobalSubject, string body = GlobalBody, bool isActive = true) =>
        Create(GlobalId, doctorId: null, ReminderTemplate.AutomaticReminderName, subject, body, isActive);

    public static ReminderTemplate Create(
        int id, int? doctorId, string name, string subject, string body, bool isActive = true)
    {
        var template = (ReminderTemplate)Activator.CreateInstance(typeof(ReminderTemplate), nonPublic: true)!;
        Set(template, nameof(Entity.Id), id, typeof(Entity));
        Set(template, nameof(ReminderTemplate.DoctorId), doctorId);
        Set(template, nameof(ReminderTemplate.Name), name);
        Set(template, nameof(ReminderTemplate.Subject), subject);
        Set(template, nameof(ReminderTemplate.BodyTemplate), body);
        Set(template, nameof(ReminderTemplate.IsActive), isActive);
        return template;
    }

    private static void Set(ReminderTemplate template, string property, object? value, Type? declaringType = null) =>
        (declaringType ?? typeof(ReminderTemplate)).GetProperty(property)!.SetValue(template, value);
}
