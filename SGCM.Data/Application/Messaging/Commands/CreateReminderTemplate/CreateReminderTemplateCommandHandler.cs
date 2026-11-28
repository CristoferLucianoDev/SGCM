using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Messaging.ReminderTemplates;

namespace SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;

/// <summary>Crea la plantilla del médico autenticado; las reglas de los datos las aplica el agregado.</summary>
internal sealed class CreateReminderTemplateCommandHandler(
    DoctorReminderTemplateAccess access,
    IReminderTemplateRepository templates,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateReminderTemplateCommand, ReminderTemplateDto>
{
    public async Task<Result<ReminderTemplateDto>> HandleAsync(
        CreateReminderTemplateCommand command, CancellationToken cancellationToken)
    {
        var doctorId = await access.GetDoctorIdAsync(cancellationToken);
        if (doctorId.IsFailure) return doctorId.Error;

        var created = ReminderTemplate.Create(doctorId.Value, command.Name, command.Subject, command.BodyTemplate);
        if (created.IsFailure) return created.Error;

        templates.Add(created.Value);
        // El id se genera al guardar; el decorador de transacción confirma.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ReminderTemplateDto.From(created.Value);
    }
}
