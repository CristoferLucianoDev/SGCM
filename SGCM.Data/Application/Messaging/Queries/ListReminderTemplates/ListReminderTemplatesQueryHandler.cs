using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Messaging.Queries.ListReminderTemplates;

/// <summary>Lista las plantillas que puede usar el médico autenticado.</summary>
internal sealed class ListReminderTemplatesQueryHandler(
    DoctorReminderTemplateAccess access,
    IReminderTemplateListReader reader,
    IValidator<ListReminderTemplatesQuery> validator)
    : IQueryHandler<ListReminderTemplatesQuery, PagedResult<ReminderTemplateDto>>
{
    public async Task<Result<PagedResult<ReminderTemplateDto>>> HandleAsync(
        ListReminderTemplatesQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        var doctorId = await access.GetDoctorIdAsync(cancellationToken);
        if (doctorId.IsFailure) return doctorId.Error;

        return await reader.ListAsync(doctorId.Value, query.PageNumber, query.PageSize, cancellationToken);
    }
}
