using FluentValidation;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;

/// <summary>
/// Aplica los filtros y el orden con <see cref="IAppointmentReportBuilder"/>, el mismo que usan la generación y la
/// exportación, así que las filas coinciden con las de esos casos. No escribe en el registro de reportes.
/// </summary>
internal sealed class GetAppointmentReportQueryHandler(
    IAppointmentReportBuilder builder,
    IValidator<GetAppointmentReportQuery> validator) : IQueryHandler<GetAppointmentReportQuery, AppointmentReport>
{
    public async Task<Result<AppointmentReport>> HandleAsync(
        GetAppointmentReportQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            var failure = validation.Errors[0];
            return Error.Validation(failure.ErrorCode, failure.ErrorMessage);
        }

        return await builder.BuildPageAsync(query.Criteria, query.PageNumber, query.PageSize, cancellationToken);
    }
}
