using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Contracts.Identity;
using Shouldly;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class ChangeTemporaryPasswordTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Url = "/api/auth/change-temporary-password";
    private const string Email = "temporal203@correo.com";
    private const string ForeignEmail = "ajeno203@correo.com";
    private const string Temporary = "Temporal203";
    private const string NewPassword = "  NuevaClave203  ";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cambio_atomico_conserva_sesion_y_emite_JWT_y_refresh_validos(bool hasProfile)
    {
        var seed = await SeedAsync(hasProfile);
        var before = (await FindUserAsync(Email))!;
        var originalSessions = before.Sessions.ToDictionary(s => s.Id);
        var foreignBefore = await SnapshotAsync(ForeignEmail);
        // Los ids extras del cliente no deben seleccionar al propietario ni su sesión.
        using var response = await SendAsync(Client, HttpMethod.Post,
            $"{Url}?userId={seed.ForeignUserId}&sessionId={seed.ForeignSessionId}", seed.Current.AccessToken,
            new { newPassword = NewPassword, userId = seed.ForeignUserId, doctorId = int.MaxValue, sessionId = seed.ForeignSessionId });
        AssertStatus(response, HttpStatusCode.OK);
        response.Headers.CacheControl!.NoStore.ShouldBeTrue();
        var tokens = await response.ReadLoginAsync();
        tokens.RequiresTwoFactor.ShouldBeFalse();
        tokens.PendingStep.ShouldBe(hasProfile ? null : "doctor_profile");
        tokens.AccessToken.ShouldNotBeNullOrWhiteSpace();
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        string.Equals(tokens.AccessToken, seed.Current.AccessToken, StringComparison.Ordinal).ShouldBeFalse();
        string.Equals(tokens.RefreshToken, seed.Current.RefreshToken, StringComparison.Ordinal).ShouldBeFalse();

        var user = (await FindUserAsync(Email))!;
        user.MustChangePassword.ShouldBeFalse();
        var hasher = Factory.Services.GetRequiredService<IPasswordHasher>();
        hasher.Verify(user.PasswordHash, NewPassword).ShouldBeTrue();
        hasher.Verify(user.PasswordHash, Temporary).ShouldBeFalse();
        user.Id.ShouldBe(before.Id);
        user.Email.ShouldBe(before.Email);
        user.Role.ShouldBe(UserRole.Doctor);
        user.IsActive.ShouldBeTrue();
        user.IsEmailConfirmed.ShouldBeTrue();
        user.Sessions.Select(s => s.Id).Order().ShouldBe(originalSessions.Keys.Order());
        var current = user.Sessions.Single(s => s.Id == seed.SessionId);
        string.Equals(current.RefreshTokenHash, Factory.Services.GetRequiredService<ISecureTokenGenerator>().Hash(tokens.RefreshToken!),
            StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(current.PreviousRefreshTokenHash, originalSessions[seed.SessionId].RefreshTokenHash,
            StringComparison.Ordinal).ShouldBeTrue();
        current.DeviceName.ShouldBe(originalSessions[seed.SessionId].DeviceName);
        current.IpAddress.ShouldBe(originalSessions[seed.SessionId].IpAddress);
        current.LastUsedAt.ShouldBe(Clock.UtcNow);
        current.ExpiresAt.ShouldBe(tokens.RefreshTokenExpiresAt!.Value);
        current.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
        var jwtOptions = Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value;
        current.ExpiresAt.ShouldBe(Clock.UtcNow.AddDays(jwtOptions.RefreshTokenDays));
        tokens.AccessTokenExpiresAt.ShouldBe(Clock.UtcNow.AddMinutes(jwtOptions.AccessTokenMinutes));
        foreach (var session in user.Sessions.Where(s => s.Id != seed.SessionId))
        {
            string.Equals(session.RefreshTokenHash, originalSessions[session.Id].RefreshTokenHash,
                StringComparison.Ordinal).ShouldBeTrue();
            string.Equals(session.PreviousRefreshTokenHash, originalSessions[session.Id].PreviousRefreshTokenHash,
                StringComparison.Ordinal).ShouldBeTrue();
            session.LastUsedAt.ShouldBe(originalSessions[session.Id].LastUsedAt);
            session.ExpiresAt.ShouldBe(originalSessions[session.Id].ExpiresAt);
            session.RevocationReason.ShouldBe(SessionRevocationReason.PasswordReset);
            session.RevokedAt.ShouldBe(Clock.UtcNow);
        }
        AssertSnapshotUnchanged(await SnapshotAsync(ForeignEmail), foreignBefore);
        var jwt = Read(tokens);
        var old = Read(seed.Current);
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(user.Id.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.SessionId).Value.ShouldBe(seed.SessionId.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
        jwt.Issuer.ShouldBe(jwtOptions.Issuer);
        jwt.Audiences.ShouldContain(jwtOptions.Audience);
        jwt.ValidTo.ShouldBe(tokens.AccessTokenExpiresAt!.Value.UtcDateTime);
        jwt.GetClaim(AppClaims.TokenId).Value.ShouldNotBe(old.GetClaim(AppClaims.TokenId).Value);
        if (hasProfile) jwt.TryGetClaim(AppClaims.PendingStep, out _).ShouldBeFalse();
        else jwt.GetClaim(AppClaims.PendingStep).Value.ShouldBe("doctor_profile");
        (await ValidateJwtAsync(tokens.AccessToken!)).ShouldBeTrue();
        (await ValidateJwtAsync(seed.Current.AccessToken!)).ShouldBeTrue();
        old.GetClaim(AppClaims.PendingStep).Value.ShouldBe("change_password");

        using var otherRefresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = seed.Other.RefreshToken });
        AssertStatus(otherRefresh, HttpStatusCode.Unauthorized);
        using var refreshed = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        AssertStatus(refreshed, HttpStatusCode.OK);
        var rotated = await refreshed.Content.ReadFromJsonAsync<RefreshSessionResponse>();
        rotated!.PendingStep.ShouldBe(tokens.PendingStep);
        new JsonWebTokenHandler().ReadJsonWebToken(rotated.AccessToken).GetClaim(AppClaims.SessionId).Value
            .ShouldBe(seed.SessionId.ToString(CultureInfo.InvariantCulture));
        using var oldLogin = await LoginAsync(Email, Temporary);
        AssertStatus(oldLogin, HttpStatusCode.Unauthorized);
        using var newLogin = await LoginAsync(Email, NewPassword);
        AssertStatus(newLogin, HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(null, "User.PasswordTooShort")]
    [InlineData("", "User.PasswordTooShort")]
    [InlineData("C\"ñ1234", "User.PasswordTooShort")]
    [InlineData("long", "User.PasswordTooLong")]
    [InlineData(Temporary, "User.PasswordUnchanged")]
    public async Task Entrada_rechazada_no_persiste_cambios(string? password, string error)
    {
        var seed = await SeedAsync();
        var before = await SnapshotAsync();
        if (password == "long") password = new string('x', 129);
        using var response = await ChangeAsync(seed.Current.AccessToken, password);
        await AssertErrorAsync(response, HttpStatusCode.BadRequest, error);
        if (!string.IsNullOrEmpty(password))
        {
            var payload = await response.Content.ReadAsByteArrayAsync();
            ContainsJsonText(payload, password)
                .ShouldBeFalse("La respuesta de rechazo no debe reproducir la contraseña candidata.");
        }
        AssertSnapshotUnchanged(await SnapshotAsync(), before);
    }

    [Theory]
    [InlineData("none", 401)]
    [InlineData("invalid", 401)]
    [InlineData("patient", 403)]
    [InlineData("administrator", 403)]
    [InlineData("wrong-step", 403)]
    public async Task Autorizacion_rechaza_sin_mutaciones(string kind, int status)
    {
        var seed = await SeedAsync();
        var token = kind switch
        {
            "none" => null,
            "invalid" => "jwt-invalido",
            "patient" => Token(seed.UserId, seed.SessionId, UserRole.Patient),
            "administrator" => Token(seed.UserId, seed.SessionId, UserRole.Administrator),
            _ => Token(seed.UserId, seed.SessionId, UserRole.Doctor, PendingStep.DoctorProfile)
        };
        var before = await SnapshotAsync();
        using var response = await ChangeAsync(token);
        AssertStatus(response, (HttpStatusCode)status);
        AssertSnapshotUnchanged(await SnapshotAsync(), before);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("foreign")]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("inactive")]
    [InlineData("unknown-user")]
    [InlineData("zero-sid")]
    public async Task Identidad_o_estado_invalido_no_produce_cambio_parcial(string kind)
    {
        var seed = await SeedAsync();
        var token = seed.Current.AccessToken;
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (kind == "expired")
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE app.user_session SET expires_at = {Clock.UtcNow} WHERE id = {seed.SessionId}");
            if (kind == "revoked")
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE app.user_session SET revoked_at = {Clock.UtcNow}, revocation_reason = 'logout' WHERE id = {seed.SessionId}");
            if (kind == "inactive")
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE app.\"user\" SET is_active = false WHERE id = {seed.UserId}");
        }
        if (kind == "missing") token = Token(seed.UserId, int.MaxValue);
        if (kind == "foreign") token = Token(seed.UserId, seed.ForeignSessionId);
        if (kind == "unknown-user") token = Token(int.MaxValue, seed.SessionId);
        if (kind == "zero-sid") token = Token(seed.UserId, 0);
        var before = await SnapshotAsync();
        using var response = await ChangeAsync(token);
        await AssertErrorAsync(response, kind == "inactive" ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
            kind == "inactive" ? "User.AccountInactive" : "User.SessionInvalid");
        AssertSnapshotUnchanged(await SnapshotAsync(), before);
    }

    [Fact]
    public async Task Segundo_cambio_se_rechaza_y_los_tokens_pendientes_no_abren_otros_endpoints()
    {
        var seed = await SeedAsync();
        using (var blocked = await SendAsync(Client, HttpMethod.Post, "/api/doctors/me/specialties/11", seed.Current.AccessToken))
            AssertStatus(blocked, HttpStatusCode.Forbidden);
        using var first = await ChangeAsync(seed.Current.AccessToken);
        AssertStatus(first, HttpStatusCode.OK);
        var tokens = await first.ReadLoginAsync();
        var before = await SnapshotAsync();
        using var second = await ChangeAsync(seed.Current.AccessToken);
        await AssertErrorAsync(second, HttpStatusCode.Conflict, "User.TemporaryPasswordChangeNotRequired");
        using var profileBlocked = await ChangeAsync(tokens.AccessToken);
        AssertStatus(profileBlocked, HttpStatusCode.Forbidden);
        AssertSnapshotUnchanged(await SnapshotAsync(), before);
    }

    [Fact]
    public async Task Refresh_antes_del_cambio_conserva_el_paso_y_el_bloqueo()
    {
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await SaveUserAsync(UserRole.Doctor, Email, Temporary, mustChangePassword: true);
        var userId = (await FindUserAsync(Email)).ShouldNotBeNull().Id;
        await using var scope = Factory.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var asociada = Specialty.Create("Especialidad asociada 205", "Especialidad inicial.").Value;
        var disponible = Specialty.Create("Especialidad disponible 205", "Disponible para asociar.").Value;
        int doctorId;
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            context.Specialties.AddRange(asociada, disponible);
            await context.SaveChangesAsync();
            doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, userId);
            (await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, asociada.Id)).ShouldBe(1);
            await transaction.CommitAsync();
        }

        using var loginResponse = await LoginAsync(Email, Temporary);
        AssertStatus(loginResponse, HttpStatusCode.OK);
        var login = await loginResponse.ReadLoginAsync();
        login.PendingStep.ShouldBe(PendingStepClaimValues.ChangeTemporaryPassword);
        login.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        var original = Read(login);
        original.GetClaim(AppClaims.PendingStep).Value.ShouldBe(PendingStepClaimValues.ChangeTemporaryPassword);
        var beforeRefresh = (await FindUserAsync(Email)).ShouldNotBeNull().Sessions.ShouldHaveSingleItem();
        original.GetClaim(AppClaims.SessionId).Value.ShouldBe(beforeRefresh.Id.ToString(CultureInfo.InvariantCulture));

        using var refreshed = await Client.PostAsJsonAsync("/api/auth/refresh", new RefreshSessionRequest(login.RefreshToken));
        AssertStatus(refreshed, HttpStatusCode.OK);
        var tokens = (await refreshed.Content.ReadFromJsonAsync<RefreshSessionResponse>()).ShouldNotBeNull();
        tokens.PendingStep.ShouldBe(PendingStepClaimValues.ChangeTemporaryPassword);
        tokens.RefreshToken.ShouldNotBeNullOrWhiteSpace();
        string.Equals(tokens.RefreshToken, login.RefreshToken, StringComparison.Ordinal).ShouldBeFalse();
        (await ValidateJwtAsync(tokens.AccessToken)).ShouldBeTrue();
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken);
        jwt.GetClaim(AppClaims.TokenId).Value.ShouldNotBe(original.GetClaim(AppClaims.TokenId).Value);
        jwt.GetClaim(AppClaims.UserId).Value.ShouldBe(userId.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.Role).Value.ShouldBe(nameof(UserRole.Doctor));
        jwt.GetClaim(AppClaims.SessionId).Value.ShouldBe(beforeRefresh.Id.ToString(CultureInfo.InvariantCulture));
        jwt.GetClaim(AppClaims.PendingStep).Value.ShouldBe(PendingStepClaimValues.ChangeTemporaryPassword);

        var user = (await FindUserAsync(Email)).ShouldNotBeNull();
        user.MustChangePassword.ShouldBeTrue();
        var current = user.Sessions.ShouldHaveSingleItem();
        current.Id.ShouldBe(beforeRefresh.Id);
        current.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
        current.ExpiresAt.ShouldBe(tokens.RefreshTokenExpiresAt);
        var tokenGenerator = Factory.Services.GetRequiredService<ISecureTokenGenerator>();
        string.Equals(current.RefreshTokenHash, tokenGenerator.Hash(tokens.RefreshToken), StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(current.PreviousRefreshTokenHash, beforeRefresh.RefreshTokenHash, StringComparison.Ordinal).ShouldBeTrue();

        var before = await SnapshotAsync(Email);
        var profileBefore = await TestDataSeeder.ReadDoctorsAsync(context, [doctorId]);
        var associationsBefore = await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [doctorId]);
        associationsBefore.ShouldHaveSingleItem();
        using var blocked = await SendAsync(Client, HttpMethod.Post,
            $"/api/doctors/me/specialties/{disponible.Id}", tokens.AccessToken);
        AssertStatus(blocked, HttpStatusCode.Forbidden);
        AssertSnapshotUnchanged(await SnapshotAsync(Email), before);
        (await TestDataSeeder.ReadDoctorsAsync(context, [doctorId])).ShouldBe(profileBefore);
        (await TestDataSeeder.ReadDoctorSpecialtiesAsync(context, [doctorId])).ShouldBe(associationsBefore);
    }

    [Fact]
    public async Task Refresh_anterior_respeta_gracia_y_reutilizacion_revoca_todas_las_activas()
    {
        var seed = await SeedAsync();
        using var changed = await ChangeAsync(seed.Current.AccessToken);
        AssertStatus(changed, HttpStatusCode.OK);
        var tokens = await changed.ReadLoginAsync();
        var before = await SnapshotAsync();
        using (var withinGrace = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = seed.Current.RefreshToken }))
            AssertStatus(withinGrace, HttpStatusCode.Unauthorized);
        AssertSnapshotUnchanged(await SnapshotAsync(), before);
        Clock.UtcNow = Clock.UtcNow.AddSeconds(
            Factory.Services.GetRequiredService<IOptions<JwtOptions>>().Value.RefreshReuseGraceSeconds + 1);
        using (var reused = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = seed.Current.RefreshToken }))
            AssertStatus(reused, HttpStatusCode.Unauthorized);
        var user = (await FindUserAsync(Email))!;
        user.Sessions.Single(s => s.Id == seed.SessionId).RevocationReason.ShouldBe(SessionRevocationReason.TokenReuse);
        user.Sessions.Where(s => s.Id != seed.SessionId).ShouldAllBe(s => s.RevocationReason == SessionRevocationReason.PasswordReset);
        using var newRefresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        AssertStatus(newRefresh, HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Fallo_de_tokens_o_persistencia_revierte_PostgreSQL_sin_entregar_credenciales(bool persistence)
    {
        var seed = await SeedAsync();
        var before = await SnapshotAsync();
        var capture = new TokenCapture();
        var failedIssuer = new FailingTokenService();
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            if (persistence)
            {
                services.RemoveAll<IUnitOfWork>();
                services.AddScoped<IUnitOfWork>(sp => new FailingUnitOfWork(
                    ActivatorUtilities.CreateInstance<UnitOfWork>(sp), sp.GetRequiredService<AppDbContext>(), capture));
                services.RemoveAll<ISessionService>();
                services.AddScoped<ISessionService>(sp => new CapturingSessions(
                    ActivatorUtilities.CreateInstance<SessionService>(sp), capture));
            }
            else
            {
                services.RemoveAll<ITokenService>();
                services.AddSingleton<ITokenService>(failedIssuer);
            }
        }));
        using var client = app.CreateClient();
        // El host convierte la excepción en 500: no debe entregar las credenciales preparadas.
        using var failed = await ChangeAsync(seed.Current.AccessToken, NewPassword, client);
        AssertStatus(failed, HttpStatusCode.InternalServerError);
        var failedBody = await failed.Content.ReadAsStringAsync();
        failedBody.Contains(NewPassword, StringComparison.Ordinal).ShouldBeFalse();
        if (persistence)
        {
            capture.PersistenceFailure.ShouldNotBeNull().SqlState.ShouldBe(PostgresErrorCodes.DivisionByZero);
            capture.Tokens.ShouldNotBeNull();
            failedBody.Contains(capture.Tokens.AccessToken.Value, StringComparison.Ordinal).ShouldBeFalse();
            failedBody.Contains(capture.Tokens.RefreshToken, StringComparison.Ordinal).ShouldBeFalse();
        }
        else failedIssuer.Calls.ShouldBe(1);
        AssertSnapshotUnchanged(await SnapshotAsync(), before);
        if (persistence)
        {
            using var unusable = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = capture.Tokens!.RefreshToken });
            AssertStatus(unusable, HttpStatusCode.Unauthorized);
        }
        // La sesión anterior sigue utilizable porque el cambio completo se revirtió.
        using var previous = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = seed.Current.RefreshToken });
        AssertStatus(previous, HttpStatusCode.OK);
        // Otra petición y otro scope deben poder completar el cambio después del rollback.
        using var retry = await ChangeAsync(seed.Current.AccessToken);
        AssertStatus(retry, HttpStatusCode.OK);
        var retriedTokens = await retry.ReadLoginAsync();
        var retriedUser = (await FindUserAsync(Email))!;
        retriedUser.MustChangePassword.ShouldBeFalse();
        Factory.Services.GetRequiredService<IPasswordHasher>().Verify(retriedUser.PasswordHash, NewPassword).ShouldBeTrue();
        using var retryRefresh = await Client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = retriedTokens.RefreshToken });
        AssertStatus(retryRefresh, HttpStatusCode.OK);
    }

    [Fact]
    public async Task Dos_cambios_concurrentes_esperan_bloqueo_y_solo_uno_confirma()
    {
        var seed = await SeedAsync();
        using var gate = new PasswordGate();
        var innerHasher = Factory.Services.GetRequiredService<IPasswordHasher>();
        using var app = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher>();
            services.AddScoped<IPasswordHasher>(sp => new GatedHasher(innerHasher, gate,
                sp.GetRequiredService<AppDbContext>()));
        }));
        using var client = app.CreateClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var first = ChangeAsync(seed.Current.AccessToken, NewPassword, client);
        Task<HttpResponseMessage>? second = null;
        try
        {
            var pid = await gate.Entered.Task.WaitAsync(timeout.Token);
            second = ChangeAsync(seed.Current.AccessToken, "OtraClave203", client);
            await WaitForBlockedRequestAsync(pid, timeout.Token);
            gate.VerifyCalls.ShouldBe(1);
            gate.Release.Set();
            using var a = await first;
            using var b = await second;
            AssertStatus(a, HttpStatusCode.OK);
            await AssertErrorAsync(b, HttpStatusCode.Conflict, "User.TemporaryPasswordChangeNotRequired");
            gate.VerifyCalls.ShouldBe(1);
            var tokens = await a.ReadLoginAsync();
            var user = (await FindUserAsync(Email))!;
            user.MustChangePassword.ShouldBeFalse();
            innerHasher.Verify(user.PasswordHash, NewPassword).ShouldBeTrue();
            innerHasher.Verify(user.PasswordHash, "OtraClave203").ShouldBeFalse();
            user.Sessions.Count.ShouldBe(2);
            var current = user.Sessions.Single(s => s.Id == seed.SessionId);
            current.IsActiveAt(Clock.UtcNow).ShouldBeTrue();
            string.Equals(current.RefreshTokenHash, Factory.Services.GetRequiredService<ISecureTokenGenerator>().Hash(tokens.RefreshToken!),
                StringComparison.Ordinal).ShouldBeTrue();
            user.Sessions.Single(s => s.Id != seed.SessionId).RevocationReason.ShouldBe(SessionRevocationReason.PasswordReset);
        }
        finally
        {
            gate.Release.Set();
            Task<HttpResponseMessage>[] requests = second is null ? [first] : [first, second];
            try { await Task.WhenAll(requests); }
            finally
            {
                // WhenAll espera ambas incluso si una falla; también se liberan las respuestas
                // exitosas cuando la otra petición produjo una excepción.
                foreach (var request in requests)
                    if (request.IsCompletedSuccessfully) (await request).Dispose();
            }
        }
    }

    private async Task WaitForBlockedRequestAsync(int blockingPid, CancellationToken cancellationToken)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        while (!await context.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE {blockingPid} = ANY(pg_blocking_pids(pid))) AS \"Value\"")
            .SingleAsync(cancellationToken))
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
    }

    private sealed record Seed(int UserId, int SessionId, int ForeignUserId, int ForeignSessionId,
        LoginResponse Current, LoginResponse Other);

    private async Task<Seed> SeedAsync(bool hasProfile = false)
    {
        // PostgreSQL conserva microsegundos. Usar segundos exactos evita diferencias de ticks
        // al comparar las fechas del agregado con sus valores persistidos.
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await SaveUserAsync(UserRole.Doctor, Email, Temporary, mustChangePassword: true);
        await SaveUserAsync(UserRole.Doctor, ForeignEmail, Temporary, mustChangePassword: true);
        var foreign = await LoginSuccessfullyAsync(ForeignEmail, Temporary);
        var other = await LoginSuccessfullyAsync(Email, Temporary);
        var current = await LoginSuccessfullyAsync(Email, Temporary);
        current.PendingStep.ShouldBe("change_password");
        var userId = int.Parse(Read(current).GetClaim(AppClaims.UserId).Value, CultureInfo.InvariantCulture);
        var sessionId = int.Parse(Read(current).GetClaim(AppClaims.SessionId).Value, CultureInfo.InvariantCulture);
        var foreignSessionId = int.Parse(Read(foreign).GetClaim(AppClaims.SessionId).Value, CultureInfo.InvariantCulture);
        var foreignUserId = int.Parse(Read(foreign).GetClaim(AppClaims.UserId).Value, CultureInfo.InvariantCulture);
        userId.ShouldNotBe(sessionId);
        userId.ShouldNotBe(foreignUserId);
        sessionId.ShouldNotBe(foreignSessionId);
        if (hasProfile)
        {
            using var scope = Factory.Services.CreateScope();
            await TestDataSeeder.InsertDoctorProfileAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), userId);
        }
        return new(userId, sessionId, foreignUserId, foreignSessionId, current, other);
    }

    private string Token(int userId, int sid, UserRole role = UserRole.Doctor,
        PendingStep? step = PendingStep.ChangeTemporaryPassword) =>
        Factory.Services.GetRequiredService<ITokenService>().CreateAccessToken(userId, role, sid, step).Value;

    private static JsonWebToken Read(LoginResponse response) =>
        new JsonWebTokenHandler().ReadJsonWebToken(response.AccessToken!);

    private async Task<bool> ValidateJwtAsync(string token) =>
        (await new JsonWebTokenHandler().ValidateTokenAsync(token,
            Factory.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
                .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters)).IsValid;

    private Task<HttpResponseMessage> ChangeAsync(string? token, string? password = NewPassword, HttpClient? client = null) =>
        SendAsync(client ?? Client, HttpMethod.Post, Url, token, new ChangeTemporaryPasswordRequest(password!));

    private static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url,
        string? token, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = JsonContent.Create(body);
        return SendAndDisposeAsync(client, request);
    }

    private static async Task<HttpResponseMessage> SendAndDisposeAsync(HttpClient client, HttpRequestMessage request)
    {
        using (request) return await client.SendAsync(request);
    }

    // El helper general imprime el cuerpo ante un status inesperado; un 200 inesperado
    // podría revelar credenciales. Aquí se informa solo el status, sin el contenido.
    private static void AssertStatus(HttpResponseMessage response, HttpStatusCode expected) =>
        response.StatusCode.ShouldBe(expected);

    private static async Task AssertErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        AssertStatus(response, status);
        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = payload.RootElement;
        string.Equals(body.GetProperty("code").GetString(), code, StringComparison.Ordinal)
            .ShouldBeTrue($"Se esperaba el código de error {code}.");
        body.TryGetProperty("accessToken", out _).ShouldBeFalse();
        body.TryGetProperty("refreshToken", out _).ShouldBeFalse();
    }

    private static bool ContainsJsonText(ReadOnlySpan<byte> payload, string text)
    {
        var reader = new Utf8JsonReader(payload);
        while (reader.Read())
        {
            if (reader.TokenType is JsonTokenType.String or JsonTokenType.PropertyName
                && reader.GetString()!.Contains(text, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // Snapshot de todas las columnas persistidas, sin comparar referencias a entidades tracking.
    private static void AssertSnapshotUnchanged(string actual, string expected) =>
        string.Equals(actual, expected, StringComparison.Ordinal).ShouldBeTrue(
            "El estado persistido debe conservarse; se omiten hashes y tokens de la salida de aserción.");

    private async Task<string> SnapshotAsync(string? email = null)
    {
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(u)::text AS \"Value\" FROM app.\"user\" u WHERE ({email}::text IS NULL OR u.email = {email}) ORDER BY u.id").ToListAsync();
        var sessions = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(s)::text AS \"Value\" FROM app.user_session s JOIN app.\"user\" u ON u.id = s.user_id WHERE ({email}::text IS NULL OR u.email = {email}) ORDER BY s.id").ToListAsync();
        var tokens = await context.Database.SqlQuery<string>(
            $"SELECT to_jsonb(t)::text AS \"Value\" FROM app.user_token t JOIN app.\"user\" u ON u.id = t.user_id WHERE ({email}::text IS NULL OR u.email = {email}) ORDER BY t.id").ToListAsync();
        return string.Join("\n", users.Concat(sessions).Concat(tokens));
    }

    private sealed class FailingTokenService : ITokenService
    {
        public int Calls { get; private set; }
        public AccessToken CreateAccessToken(int userId, UserRole role, int sessionId, PendingStep? pendingStep)
        {
            Calls++;
            throw new InvalidOperationException("Fallo controlado al preparar el JWT.");
        }
    }

    // El error proviene de PostgreSQL DESPUÉS de guardar las actualizaciones dentro de la
    // transacción real. La liberación sin commit debe revertir también ese SaveChanges.
    private sealed class FailingUnitOfWork(IUnitOfWork inner, AppDbContext context, TokenCapture capture) : IUnitOfWork
    {
        public Task<ITransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
            inner.BeginTransactionAsync(cancellationToken);
        public async Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            await inner.SaveChangesAsync(cancellationToken);
            try { await context.Database.ExecuteSqlRawAsync("SELECT 1 / 0", cancellationToken); }
            catch (PostgresException error) { capture.PersistenceFailure = error; throw; }
        }
    }

    private sealed class TokenCapture
    {
        public SessionTokens? Tokens { get; set; }
        public PostgresException? PersistenceFailure { get; set; }
    }

    private sealed class CapturingSessions(ISessionService inner, TokenCapture capture) : ISessionService
    {
        public async Task<Result<SessionTokens>> RenewCurrentAsync(User user, int currentSessionId,
            DateTimeOffset now, CancellationToken cancellationToken)
        {
            var result = await inner.RenewCurrentAsync(user, currentSessionId, now, cancellationToken);
            if (result.IsSuccess) capture.Tokens = result.Value;
            return result;
        }
        public Task<Result<SessionTokens>> StartAsync(User user, string? deviceName, string? ipAddress,
            CancellationToken cancellationToken) => inner.StartAsync(user, deviceName, ipAddress, cancellationToken);
        public Task<Result<SessionTokens>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
            inner.RefreshAsync(refreshToken, cancellationToken);
        public Task EndAsync(string refreshToken, CancellationToken cancellationToken) => inner.EndAsync(refreshToken, cancellationToken);
    }

    private sealed class PasswordGate : IDisposable
    {
        public TaskCompletionSource<int> Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public int VerifyCalls;
        public void Dispose() => Release.Dispose();
    }

    private sealed class GatedHasher(IPasswordHasher inner, PasswordGate gate, AppDbContext context) : IPasswordHasher
    {
        public string Hash(Password password) => inner.Hash(password);
        public bool Verify(string? passwordHash, string password)
        {
            if (Interlocked.Increment(ref gate.VerifyCalls) == 1)
            {
                gate.Entered.TrySetResult(((NpgsqlConnection)context.Database.GetDbConnection()).ProcessID);
                if (!gate.Release.Wait(TimeSpan.FromSeconds(30))) throw new TimeoutException("No se liberó el bloqueo de la prueba.");
            }
            return inner.Verify(passwordHash, password);
        }
    }
}

