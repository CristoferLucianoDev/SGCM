using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Data.Infrastructure.Persistence.Repositories.Identity;

internal sealed class UserRepository(AppDbContext context) : IUserRepository
{
    // Clave foránea sombra de tokens y sesiones; debe coincidir con HasForeignKey en UserConfiguration.
    private const string UserIdProperty = "UserId";

    public Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken) =>
        UsersWithChildren()
            .SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public Task<User?> GetByTokenHashAsync(UserTokenType type, string tokenHash, CancellationToken cancellationToken) =>
        UsersWithChildren()
            .SingleOrDefaultAsync(HasToken(type, tokenHash), cancellationToken);

    public async Task<User?> GetByTokenHashForUpdateAsync(
        UserTokenType type,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();

        var userId = await context.Users
            .Where(HasToken(type, tokenHash))
            .Select(user => (int?)user.Id)
            .SingleOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return null;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.\"user\" WHERE id = {userId.Value} FOR UPDATE",
            cancellationToken);

        // Se recarga en una sentencia nueva: en READ COMMITTED ve el token ya usado si otra
        // transacción lo consumió mientras esta esperaba el bloqueo.
        return await UsersWithChildren()
            .SingleOrDefaultAsync(user => user.Id == userId.Value, cancellationToken);
    }

    public async Task<User?> GetBySessionRefreshTokenHashForUpdateAsync(
        string refreshTokenHash,
        CancellationToken cancellationToken)
    {
        EnsureTransaction();

        // El hash anterior no es único en la base, pero basta con el primero: son hashes de
        // tokens aleatorios de 64 bytes, así que en la práctica solo pertenecen a un usuario.
        var userId = await context.Users
            .Where(user => user.Sessions.Any(session =>
                session.RefreshTokenHash == refreshTokenHash
                || session.PreviousRefreshTokenHash == refreshTokenHash))
            .Select(user => (int?)user.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (userId is null)
        {
            return null;
        }

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.\"user\" WHERE id = {userId.Value} FOR UPDATE",
            cancellationToken);

        // Se recarga en una sentencia nueva: en READ COMMITTED ve el hash ya rotado si otra
        // transacción renovó la sesión mientras esta esperaba el bloqueo.
        return await UsersWithChildren()
            .SingleOrDefaultAsync(user => user.Id == userId.Value, cancellationToken);
    }

    public async Task<User?> GetByEmailForUpdateAsync(Email email, CancellationToken cancellationToken)
    {
        EnsureTransaction();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.\"user\" WHERE email = {email.Value} FOR UPDATE",
            cancellationToken);

        // Si la limpieza borró la fila antes de tomar el bloqueo, la consulta devuelve null.
        return await GetByEmailAsync(email, cancellationToken);
    }

    public async Task<User?> GetByIdForUpdateAsync(int userId, CancellationToken cancellationToken)
    {
        EnsureTransaction();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM app.\"user\" WHERE id = {userId} FOR UPDATE",
            cancellationToken);

        return await UsersWithChildren()
            .SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public async Task<int> DeleteExpiredPendingRegistrationsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        EnsureTransaction();

        var candidateIds = await context.Users
            .Where(IsExpiredPendingRegistration(now))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return 0;
        }

        var lockedIds = await LockSkippingLockedAsync(candidateIds.ToArray(), cancellationToken);

        if (lockedIds.Count == 0)
        {
            return 0;
        }

        // SKIP LOCKED salta las filas que un reenvío tiene tomadas, y este borrado vuelve a
        // verificar la condición en una sentencia nueva: en READ COMMITTED cada sentencia ve
        // los commits más recientes, así que no se borra un registro que acaba de recibir un
        // enlace vigente.
        return await context.Users
            .Where(user => lockedIds.Contains(user.Id))
            .Where(IsExpiredPendingRegistration(now))
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task<int> DeleteEndedCredentialsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        EnsureTransaction();

        var tokenOwners = context.Set<UserToken>()
            .Where(TokenEndedBefore(cutoff))
            .Select(token => EF.Property<int>(token, UserIdProperty));
        var sessionOwners = context.Set<UserSession>()
            .Where(SessionEndedBefore(cutoff))
            .Select(session => EF.Property<int>(session, UserIdProperty));

        var candidateIds = await context.Users
            .Where(user => user.IsEmailConfirmed && (tokenOwners.Contains(user.Id) || sessionOwners.Contains(user.Id)))
            .Select(user => user.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            return 0;
        }

        // Se bloquea al usuario porque un comando que lo tiene tomado puede invalidar un token o
        // revocar una sesión ya terminada; si este proceso borrara esa fila, su UPDATE fallaría
        // por concurrencia.
        // Los usuarios bloqueados quedan tomados solo mientras corren los dos DELETE. No se procesa
        // por lotes porque el job corre una vez al día y el volumen es bajo; los filtros con OR
        // recorren ambas tablas completas, lo que se acepta por la misma razón. Si el volumen crece,
        // la salida es procesar por lotes e indexar expires_at.
        var lockedIds = await LockSkippingLockedAsync(candidateIds.ToArray(), cancellationToken);

        if (lockedIds.Count == 0)
        {
            return 0;
        }

        var deletedTokens = await context.Set<UserToken>()
            .Where(token => lockedIds.Contains(EF.Property<int>(token, UserIdProperty)))
            .Where(TokenEndedBefore(cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        var deletedSessions = await context.Set<UserSession>()
            .Where(session => lockedIds.Contains(EF.Property<int>(session, UserIdProperty)))
            .Where(SessionEndedBefore(cutoff))
            .ExecuteDeleteAsync(cancellationToken);

        return deletedTokens + deletedSessions;
    }

    public void Add(User user) => context.Users.Add(user);

    // El agregado se carga completo porque sus reglas (límite de sesiones, invalidar tokens)
    // necesitan ver todos sus hijos. AsSplitQuery evita el producto cartesiano de dos
    // colecciones en un mismo JOIN. El volumen de sesiones queda acotado porque un proceso
    // periódico borra las vencidas o revocadas con más del período de retención
    // (Accounts:CredentialRetentionDays, 30 días por defecto) (modelo de datos, sección 8).
    private IQueryable<User> UsersWithChildren() =>
        context.Users
            .Include(user => user.Tokens)
            .Include(user => user.Sessions)
            .AsSplitQuery();

    private Task<List<int>> LockSkippingLockedAsync(int[] candidateIds, CancellationToken cancellationToken) =>
        context.Database
            .SqlQuery<int>($"SELECT id AS \"Value\" FROM app.\"user\" WHERE id = ANY({candidateIds}) FOR UPDATE SKIP LOCKED")
            .ToListAsync(cancellationToken);

    private static Expression<Func<User, bool>> HasToken(UserTokenType type, string tokenHash) =>
        user => user.Tokens.Any(token => token.Type == type && token.TokenHash == tokenHash);

    private static Expression<Func<User, bool>> IsExpiredPendingRegistration(DateTimeOffset now) =>
        user => !user.IsEmailConfirmed
            && !user.Tokens.Any(token => token.Type == UserTokenType.EmailConfirmation
                && token.UsedAt == null
                && token.InvalidatedAt == null
                && token.ExpiresAt > now);

    // Misma regla que UserToken.EndedBefore y UserSession.EndedBefore, escrita como expresión
    // para que EF la traduzca a SQL.
    private static Expression<Func<UserToken, bool>> TokenEndedBefore(DateTimeOffset cutoff) =>
        token => token.ExpiresAt < cutoff || token.UsedAt < cutoff || token.InvalidatedAt < cutoff;

    private static Expression<Func<UserSession, bool>> SessionEndedBefore(DateTimeOffset cutoff) =>
        session => session.ExpiresAt < cutoff || session.RevokedAt < cutoff;

    private void EnsureTransaction()
    {
        if (context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "El bloqueo de filas requiere una transacción activa; sin ella el bloqueo se libera al terminar la sentencia.");
        }
    }
}