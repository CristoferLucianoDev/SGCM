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

        await using (var context = CreateContext())
        {
            var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);
            deleted.ShouldBe(1);
        }

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

        await using (var context = CreateContext())
        {
            var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);
            deleted.ShouldBe(0);
        }

        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(user => user.Email == email)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_conserva_la_cuenta_confirmada()
    {
        var email = Email.Create("confirmada@correo.com").Value;
        await SaveAsync(UserMother.Confirmed(email));
        var now = UserMother.Now.AddDays(1);

        await using (var context = CreateContext())
        {
            var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);
            deleted.ShouldBe(0);
        }

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

        await using var context = CreateContext();
        var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);

        deleted.ShouldBe(0);
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_ignora_tokens_de_otro_tipo()
    {
        var user = UserMother.Pending(Email.Create("restablecer@correo.com").Value);
        user.IssueToken(UserTokenType.PasswordReset, "raw-reset", "hash-reset", UserMother.Now.AddMinutes(10), TimeSpan.FromMinutes(30));
        await SaveAsync(user);
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        await using var context = CreateContext();
        var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);

        deleted.ShouldBe(1);
    }

    [Fact]
    public async Task DeleteExpiredPendingRegistrationsAsync_devuelve_solo_la_cantidad_borrada()
    {
        var confirmedEmail = Email.Create("confirmada@correo.com").Value;
        await SaveAsync(UserMother.Pending(Email.Create("pendiente-1@correo.com").Value));
        await SaveAsync(UserMother.Pending(Email.Create("pendiente-2@correo.com").Value));
        await SaveAsync(UserMother.Confirmed(confirmedEmail));
        var now = UserMother.Now + UserMother.ConfirmationLifetime;

        await using (var context = CreateContext())
        {
            var deleted = await new UserRepository(context).DeleteExpiredPendingRegistrationsAsync(now, CancellationToken.None);
            deleted.ShouldBe(2);
        }

        await using var verification = CreateContext();
        (await verification.Users.AnyAsync(user => user.Email == confirmedEmail)).ShouldBeTrue();
    }

    private async Task SaveAsync(User user)
    {
        await using var context = CreateContext();
        new UserRepository(context).Add(user);
        await context.SaveChangesAsync();
    }

    private AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString)
            .UseSnakeCaseNamingConvention()
            .Options);
}