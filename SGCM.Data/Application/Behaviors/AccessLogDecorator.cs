using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Clinical;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;

namespace SGCM.Data.Application.Behaviors;

/// <summary>
/// Registra en <c>clinical.medical_record_access_log</c> cada acceso al registro clínico que el handler informó al
/// <see cref="MedicalRecordAccessTracker"/>. Corre dentro de la transacción del comando: si el handler falla no se
/// registra nada, y si falla el registro se revierte también el cambio clínico.
/// </summary>
internal sealed class AccessLogDecorator<TCommand, TResponse>(
    ICommandHandler<TCommand, TResponse> inner,
    MedicalRecordAccessTracker tracker,
    IMedicalRecordAccessLogRepository repository,
    IUnitOfWork unitOfWork,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : ICommandHandler<TCommand, TResponse>
    where TCommand : IAccessLoggedCommand<TResponse>
{
    public async Task<Result<TResponse>> HandleAsync(TCommand command, CancellationToken cancellationToken)
    {
        var result = await inner.HandleAsync(command, cancellationToken);
        if (result.IsFailure) return result;

        // Un comando de este tipo que termina bien siempre accedió a algún registro; si no lo informó, el acceso
        // quedaría sin constancia, y eso es un error de programación del handler.
        if (tracker.Accesses.Count == 0)
            throw new InvalidOperationException(
                $"{typeof(TCommand).Name} terminó bien sin informar ningún acceso al registro clínico.");

        // Las versiones nuevas reciben su id al guardarse; el TransactionDecorator vuelve a guardar y confirma.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var accessedAt = clock.UtcNow;
        foreach (var access in tracker.Accesses)
        {
            repository.Add(MedicalRecordAccessLog.Record(
                access.Record.Id, currentUser.UserId, access.AccessType, accessedAt, command.IpAddress));
        }

        tracker.Clear();
        return result;
    }
}
