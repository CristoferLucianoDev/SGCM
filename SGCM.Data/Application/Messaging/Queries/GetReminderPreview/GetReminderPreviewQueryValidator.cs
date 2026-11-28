using FluentValidation;

namespace SGCM.Data.Application.Messaging.Queries.GetReminderPreview;

/// <summary>Valida la forma de <see cref="GetReminderPreviewQuery"/>: los dos identificadores deben ser mayores que cero.</summary>
public sealed class GetReminderPreviewQueryValidator : AbstractValidator<GetReminderPreviewQuery>
{
    public GetReminderPreviewQueryValidator()
    {
        RuleFor(query => query.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(query => query.TemplateId)
            .GreaterThan(0)
            .WithErrorCode("ReminderTemplate.InvalidId")
            .WithMessage("El identificador de la plantilla debe ser mayor que cero.");
    }
}
