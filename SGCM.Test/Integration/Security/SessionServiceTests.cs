using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Security;

/// <summary>
/// Prueba de que <see cref="ISessionService"/> guarda la sesión con el cableado real y que
/// el access token lleva su id en el claim sid.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SessionServiceTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string DeviceName = "Chrome en Windows";
    private const string IpAddress = "203.0.113.10";

    [Fact]
    public async Task StartAsync_guarda_la_sesion_y_el_sid_es_su_id()
    {
        await SaveAsync(UserMother.Confirmed());

        var tokens = await StartSessionAsync();

        using var scope = Factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);
        var session = user!.Sessions.ShouldHaveSingleItem();

        var accessToken = new JsonWebTokenHandler().ReadJsonWebToken(tokens.AccessToken.Value);
        accessToken.GetClaim(AppClaims.SessionId).Value.ShouldBe(session.Id.ToString(CultureInfo.InvariantCulture));

        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecureTokenGenerator>();
        session.RefreshTokenHash.ShouldBe(tokenGenerator.Hash(tokens.RefreshToken));
        (await CountRowsContainingAsync(scope, tokens.RefreshToken)).ShouldBe(0);
    }

    private async Task<SessionTokens> StartSessionAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<IUserRepository>()
            .GetByEmailAsync(UserMother.DefaultEmail, CancellationToken.None);

        var result = await scope.ServiceProvider.GetRequiredService<ISessionService>()
            .StartAsync(user!, DeviceName, IpAddress, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        return result.Value;
    }

    // La fila convertida a texto incluye todas sus columnas: si el token en claro aparece en
    // alguna, la fila cuenta.
    private static Task<int> CountRowsContainingAsync(IServiceScope scope, string value) =>
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database
            .SqlQuery<int>($"SELECT count(*)::int AS \"Value\" FROM app.user_session s WHERE strpos(s::text, {value}) > 0")
            .SingleAsync();
}
