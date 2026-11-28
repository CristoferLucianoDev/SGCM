using FluentValidation;

namespace SGCM.Data.Application.Messaging.Queries.ListReminderTemplates;

/// <summary>Valida la paginación de <see cref="ListReminderTemplatesQuery"/>, con las mismas reglas que las demás listas.</summary>
public sealed class ListReminderTemplatesQueryValidator : AbstractValidator<ListReminderTemplatesQuery>
{
    public ListReminderTemplatesQueryValidator()
    {
        RuleFor(query => query.PageNumber)
            .GreaterThanOrEqualTo(1)
            .WithErrorCode("ReminderTemplate.InvalidPageNumber")
            .WithMessage("El número de página debe ser mayor o igual a 1.");

        RuleFor(query => query.PageSize)
            .InclusiveBetween(1, 100)
            .WithErrorCode("ReminderTemplate.InvalidPageSize")
            .WithMessage("El tamaño de página debe estar entre 1 y 100.");

        RuleFor(query => query.PageNumber)
            .Must((query, page) => ((long)page - 1) * query.PageSize <= int.MaxValue)
            .When(query => query.PageNumber >= 1 && query.PageSize is >= 1 and <= 100)
            .WithErrorCode("ReminderTemplate.PageOffsetTooLarge")
            .WithMessage("El desplazamiento de la página supera el máximo permitido.");
    }
}
