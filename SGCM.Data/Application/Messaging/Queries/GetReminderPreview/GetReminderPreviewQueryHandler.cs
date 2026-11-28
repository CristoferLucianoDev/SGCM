using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Application.Messaging.Queries.GetReminderPreview;

/// <summary>
/// Completa la plantilla elegida con <see cref="ReminderComposer"/> y los datos de la cita. Aplica las mismas reglas
/// que programar el recordatorio: cita propia confirmada o completada y plantilla activa, global o del médico. Una
/// cita ajena responde como inexistente, igual que una plantilla privada de otro médico, para no revelarlas.
/// </summary>
internal sealed class GetReminderPreviewQueryHandler(
    ICurrentUser currentUser,
    IActiveDoctorReader doctorReader,
    IAppointmentRepository appointments,
    IReminderTemplateRepository templates,
    IAppointmentNotificationReader appointmentData,
    ReminderComposer composer,
    IValidator<GetReminderPreviewQuery> validator) : IQueryHandler<GetReminderPreviewQuery, ReminderPreviewDto>
{
    public async Task<Result<ReminderPreviewDto>> HandleAsync(
        GetReminderPreviewQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        if (!currentUser.IsAuthenticated || !currentUser.IsInRole(UserRole.Doctor)) return ReminderErrors.DoctorOnly;
        var doctorId = await doctorReader.FindActiveDoctorIdByUserIdAsync(currentUser.UserId, cancellationToken);
        if (doctorId is null) return DoctorErrors.NotFound;

        var appointment = await appointments.GetByIdAsync(query.AppointmentId, cancellationToken);
        if (appointment is null || appointment.DoctorId != doctorId) return AppointmentErrors.NotFound;

        var eligible = Reminder.EnsureAppointmentEligible(appointment.Status);
        if (eligible.IsFailure) return eligible.Error;

        var template = await templates.GetByIdAsync(query.TemplateId, cancellationToken);
        if (template is null || !template.IsUsableBy(doctorId.Value)) return ReminderTemplateErrors.NotFound;

        var data = await appointmentData.GetAsync(appointment.Id, cancellationToken);
        if (data is null) return AppointmentErrors.NotFound;

        var composed = composer.Compose(template, data);
        return new ReminderPreviewDto(template.Id, composed.Subject, composed.Message);
    }
}
