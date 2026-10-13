using Microsoft.EntityFrameworkCore;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.Identity;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;
using System;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public class UserRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    // Tiempo máximo que la limpieza puede tardar mientras otra transacción tiene una fila bloqueada.
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromSeconds(10);

    private static readonly DateTimeOffset Now = UserMother.Now;

    // Vigencia de la sesión y máximo de sesiones activas que fija la arquitectura.
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
    private const int MaxSessions = 5;

    private const string SessionHash = "hash-sesion";
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task Guarda_y_recupera_un_registro_pendiente_con_su_token()
    {
        await SaveAsync(UserMother.Pending());

        await using var context = CreateContext();
        var user = await new UserRepository(context).GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);

        user.ShouldNotBeNull();
        user.Id.ShouldBeGreaterThan(0);
        user.Email.ShouldBe(UserMother.DefaultEmail);
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
        user.Role.ShouldBe(UserRole.Patient);
        user.IsEmailConfirmed.ShouldBeFalse();
        user.IsActive.ShouldBeTrue();

        var token = user.Tokens.ShouldHaveSingleItem();
        token.Type.ShouldBe(UserTokenType.EmailConfirmation);
        token.TokenHash.ShouldBe(UserMother.DefaultTokenHash);
        token.IssuedAt.ShouldBe(UserMother.Now);
        token.ExpiresAt.ShouldBe(UserMother.Now + UserMother.ConfirmationLifetime);
        token.IsValidAt(UserMother.Now).ShouldBeTrue();
    }

    [Fact]
    public async Task GetByEmailAsync_devuelve_null_si_el_correo_no_existe()
    {
        await using var context = CreateContext();

        var user = await new UserRepository(context)
            .GetByEmailAsync(Email.Create("nadie@correo.com").Value, CancellationToken.None);

        user.ShouldBeNull();
    }

    [Fact]
    public async Task GetByTokenHashAsync_devuelve_el_usuario_con_todos_sus_tokens_aunque_el_token_este_invalidado()
    {
        var pending = UserMother.Pending();
        pending.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", UserMother.Now.AddMinutes(5), UserMother.ConfirmationLifetime);
        await SaveAsync(pending);

        await using var context = CreateContext();
        var user = await new UserRepository(context)
            .GetByTokenHashAsync(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, CancellationToken.None);

        // El repositorio solo encuentra; si el token sirve lo decide el dominio.
        user.ShouldNotBeNull();
        user.Tokens.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetByTokenHashAsync_devuelve_null_si_el_hash_es_de_otro_tipo()
    {
        var pending = UserMother.Pending();
        pending.IssueToken(UserTokenType.PasswordReset, "raw-reset", "hash-reset", UserMother.Now.AddMinutes(10), TimeSpan.FromMinutes(30));
        await SaveAsync(pending);

        await using var context = CreateContext();
        var user = await new UserRepository(context)
            .GetByTokenHashAsync(UserTokenType.EmailConfirmation, "hash-reset", CancellationToken.None);

        user.ShouldBeNull();
    }

    [Fact]
    public async Task GetByTokenHashAsync_devuelve_null_si_el_hash_no_existe()
    {
        await SaveAsync(UserMother.Pending());

        await using var context = CreateContext();
        var user = await new UserRepository(context)
            .GetByTokenHashAsync(UserTokenType.EmailConfirmation, "hash-inexistente", CancellationToken.None);

        user.ShouldBeNull();
    }

    [Fact]
    public async Task Persiste_el_reemplazo_de_un_registro_pendiente()
    {
        await SaveAsync(UserMother.Pending());
        var later = UserMother.Now.AddMinutes(5);

        await using (var context = CreateContext())
        {
            var user = await new UserRepository(context).GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);
            user!.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", later, UserMother.ConfirmationLifetime);
            await context.SaveChangesAsync();
        }

        await using var verification = CreateContext();
        var reloaded = await new UserRepository(verification).GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);

        reloaded!.PasswordHash.ShouldBe("nuevo-hash");
        reloaded.Tokens.Count.ShouldBe(2);
        reloaded.Tokens.Single(token => token.TokenHash == UserMother.DefaultTokenHash).InvalidatedAt.ShouldBe(later);
        reloaded.Tokens.Single(token => token.TokenHash == "hash-2").IsValidAt(later).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_borra_el_pendiente_vencido_y_sus_tokens()
    {
        var email = Email.Create("vencido@correo.com").Value;
        await SaveAsync(UserMother.Pending(email));
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        (await PurgeAsync(now)).ShouldBe(1);

        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(user => user.Email == email)).ShouldBeFalse();
        (await verification.Set<UserToken>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_conserva_el_pendiente_con_token_vigente()
    {
        var email = Email.Create("vigente@correo.com").Value;
        await SaveAsync(UserMother.Pending(email));
        var now = UserMother.Now.AddMinutes(1);

        (await PurgeAsync(now)).ShouldBe(0);

        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(user => user.Email == email)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_conserva_la_cuenta_confirmada()
    {
        var email = Email.Create("confirmada@correo.com").Value;
        await SaveAsync(UserMother.Confirmed(email));
        var now = UserMother.Now.AddDays(1);

        (await PurgeAsync(now)).ShouldBe(0);

        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(user => user.Email == email)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_conserva_el_pendiente_cuyo_ultimo_token_sigue_vigente()
    {
        var user = UserMother.Pending(Email.Create("reenviado@correo.com").Value);
        user.IssueToken(UserTokenType.EmailConfirmation, "raw-2", "hash-2", UserMother.Now.AddMinutes(10), UserMother.ConfirmationLifetime);
        await SaveAsync(user);
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        (await PurgeAsync(now)).ShouldBe(0);
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_ignora_tokens_de_otro_tipo()
    {
        var user = UserMother.Pending(Email.Create("restablecer@correo.com").Value);
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset", "hash-reset", UserMother.Now.AddMinutes(10), TimeSpan.FromMinutes(30));
        await SaveAsync(user);
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        (await PurgeAsync(now)).ShouldBe(1);
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_devuelve_solo_la_cantidad_borrada()
    {
        var confirmedEmail = Email.Create("confirmada@correo.com").Value;
        await SaveAsync(UserMother.Pending(Email.Create("pendiente-1@correo.com").Value));
        await SaveAsync(UserMother.Pending(Email.Create("pendiente-2@correo.com").Value));
        await SaveAsync(UserMother.Confirmed(confirmedEmail));
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        (await PurgeAsync(now)).ShouldBe(2);

        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(user => user.Email == confirmedEmail)).ShouldBeTrue();
    }

    [Fact]
    public async Task GetByEmailForUpdateAsync_devuelve_el_usuario_con_sus_tokens()
    {
        await SaveAsync(UserMother.Pending());

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = await new UserRepository(context).GetByEmailForUpdateAsync(UserMother.DefaultEmail, CancellationToken.None);

        user.ShouldNotBeNull();
        user.Email.ShouldBe(UserMother.DefaultEmail);
        user.Tokens.ShouldHaveSingleItem().TokenHash.ShouldBe(UserMother.DefaultTokenHash);
    }

    [Fact]
    public async Task GetByEmailForUpdateAsync_devuelve_null_si_el_correo_no_existe()
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();

        var user = await new UserRepository(context)
            .GetByEmailForUpdateAsync(Email.Create("nadie@correo.com").Value, CancellationToken.None);

        user.ShouldBeNull();
    }

    [Fact]
    public async Task GetByEmailForUpdateAsync_sin_transaccion_lanza_excepcion()
    {
        await using var context = CreateContext();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new UserRepository(context).GetByEmailForUpdateAsync(UserMother.DefaultEmail, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_sin_transaccion_lanza_excepcion()
    {
        await using var context = CreateContext();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(UserMother.Now, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_salta_el_usuario_bloqueado_por_otra_transaccion()
    {
        // Este es el caso de un reenvío en curso durante la limpieza.
        var lockedEmail = Email.Create("bloqueado@correo.com").Value;
        var freeEmail = Email.Create("libre@correo.com").Value;
        await SaveAsync(UserMother.Pending(lockedEmail));
        await SaveAsync(UserMother.Pending(freeEmail));
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        await using (var resendContext = CreateContext())
        {
            await using var resendTransaction = await resendContext.Database.BeginTransactionAsync();
            (await new UserRepository(resendContext).GetByEmailForUpdateAsync(lockedEmail, CancellationToken.None)).ShouldNotBeNull();

            (await PurgeWithTimeoutAsync(now)).ShouldBe(1);

            await using (var verification = CreateContext())
            {
                (await verification.Users.AnyAsync(user => user.Email == freeEmail)).ShouldBeFalse();
                (await verification.Users.AnyAsync(user => user.Email == lockedEmail)).ShouldBeTrue();
            }

            await resendTransaction.RollbackAsync();
        }

        (await PurgeAsync(now)).ShouldBe(1);

        await using var finalVerification = CreateContext();
        (await finalVerification.Users.AnyAsync(user => user.Email == lockedEmail)).ShouldBeFalse();
    }

    [Fact]
    public async Task Persiste_una_sesion_con_sus_datos()
    {
        var confirmed = UserMother.Confirmed();
        StartSession(confirmed, SessionHash, Now);
        await SaveAsync(confirmed);

        await using var context = CreateContext();
        var user = await new UserRepository(context).GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);

        var session = user!.Sessions.ShouldHaveSingleItem();
        session.Id.ShouldBeGreaterThan(0);
        session.RefreshTokenHash.ShouldBe(SessionHash);
        session.DeviceName.ShouldBe(DeviceName);
        session.IpAddress.ShouldBe(IpAddress);
        session.ExpiresAt.ShouldBe(Now + SessionLifetime);
        session.LastUsedAt.ShouldBe(Now);
        session.RevokedAt.ShouldBeNull();
        session.RevocationReason.ShouldBeNull();
    }

    [Fact]
    public async Task Persiste_la_revocacion_por_limite_de_sesiones()
    {
        var confirmed = UserMother.Confirmed();
        for (var index = 0; index <= MaxSessions; index++)
        {
            StartSession(confirmed, $"hash-{index}", Now.AddMinutes(index));
        }

        await SaveAsync(confirmed);
        var limitAt = Now.AddMinutes(MaxSessions);

        await using var context = CreateContext();
        var user = await new UserRepository(context).GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);

        var first = user!.Sessions.Single(session => session.RefreshTokenHash == "hash-0");
        first.RevocationReason.ShouldBe(SessionRevocationReason.SessionLimit);
        first.RevokedAt.ShouldBe(limitAt);
        user.Sessions.Count(session => session.IsActiveAt(limitAt)).ShouldBe(MaxSessions);

        var storedReason = await context.Database
            .SqlQuery<string>($"SELECT revocation_reason AS \"Value\" FROM app.user_session WHERE id = {first.Id}")
            .SingleAsync();
        storedReason.ShouldBe("session_limit");
    }

    [Fact]
    public async Task GetByTokenHashAsync_carga_tambien_las_sesiones()
    {
        var confirmed = UserMother.Confirmed();
        StartSession(confirmed, SessionHash, Now);
        await SaveAsync(confirmed);

        await using var context = CreateContext();
        var user = await new UserRepository(context)
            .GetByTokenHashAsync(UserTokenType.EmailConfirmation, UserMother.DefaultTokenHash, CancellationToken.None);

        user.ShouldNotBeNull();
        user.Sessions.ShouldHaveSingleItem().RefreshTokenHash.ShouldBe(SessionHash);
    }

    [Fact]
    public async Task GetByEmailForUpdateAsync_carga_tambien_las_sesiones()
    {
        var confirmed = UserMother.Confirmed();
        StartSession(confirmed, SessionHash, Now);
        await SaveAsync(confirmed);

        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var user = await new UserRepository(context).GetByEmailForUpdateAsync(UserMother.DefaultEmail, CancellationToken.None);

        user.ShouldNotBeNull();
        user.Sessions.ShouldHaveSingleItem().RefreshTokenHash.ShouldBe(SessionHash);
    }

    private static void StartSession(User user, string refreshTokenHash, DateTimeOffset at) =>
        user.StartSession(refreshTokenHash, DeviceName, IpAddress, at, SessionLifetime, MaxSessions).IsSuccess.ShouldBeTrue();

    private async Task SaveAsync(User user)
    {
        await using var context = CreateContext();
        new UserRepository(context).Add(user);
        await context.SaveChangesAsync();
    }

    private async Task<int> PurgeAsync(DateTimeOffset now)
    {
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);
        await transaction.CommitAsync();
        return deleted;
    }

    // Si la limpieza esperara el bloqueo en vez de saltarlo, la prueba colgaría: el límite
    // la hace fallar por cancelación.
    private async Task<int> PurgeWithTimeoutAsync(DateTimeOffset now)
    {
        using var timeout = new CancellationTokenSource(LockWaitTimeout);
        await using var context = CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(timeout.Token);
        var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, timeout.Token);
        await transaction.CommitAsync(timeout.Token);
        return deleted;
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}