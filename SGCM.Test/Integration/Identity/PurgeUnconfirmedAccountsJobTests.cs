using System.Net;
using System.Net.Http.Json;
using Shouldly;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Web.Jobs;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class PurgeUnconfirmedAccountsJobTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string RegisterUrl = "/api/accounts/register";
    private const string ConfirmUrl = "/api/accounts/confirm-email";
    private const string Email = "paciente@correo.com";
    private const string Password = "Secreta123";
    private const string ExpiredEmail = "vencido@correo.com";

    // Vigencia del enlace de confirmación según el PBI.
    private static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(15);

    [Fact]
    public async Task Limpieza_borra_el_registro_vencido_y_conserva_el_vigente()
    {
        const string validEmail = "vigente@correo.com";

        await RegisterAsync(ExpiredEmail);
        Clock.UtcNow += TimeSpan.FromMinutes(10);
        await RegisterAsync(validEmail);
        Clock.UtcNow += TimeSpan.FromMinutes(5);

        await RunPurgeAsync();

        (await FindUserAsync(ExpiredEmail)).ShouldBeNull();
        var pending = await FindUserAsync(validEmail);
        pending.ShouldNotBeNull();
        pending.IsEmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task Limpieza_deja_libre_el_correo_del_registro_borrado()
    {
        await RegisterAsync(Email);
        var purgedId = (await FindUserAsync(Email))!.Id;
        Clock.UtcNow += ConfirmationLifetime;

        await RunPurgeAsync();

        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email = Email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await FindUserAsync(Email))!.Id.ShouldNotBe(purgedId);
    }

    [Fact]
    public async Task Limpieza_conserva_el_registro_cuyo_ultimo_enlace_sigue_vigente()
    {
        await RegisterAsync(ExpiredEmail);
        await RegisterAsync(Email);
        Clock.UtcNow += TimeSpan.FromMinutes(10);
        await RegisterAsync(Email);
        Clock.UtcNow += TimeSpan.FromMinutes(10);

        await RunPurgeAsync();

        // Prueba que la limpieza corrió: RunOnceAsync registra los fallos sin propagarlos.
        (await FindUserAsync(ExpiredEmail)).ShouldBeNull();
        (await FindUserAsync(Email)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Limpieza_conserva_las_cuentas_confirmadas()
    {
        await RegisterAsync(ExpiredEmail);
        await RegisterAsync(Email);
        await ConfirmAsync(Factory.ConfirmationTokensFor(Email).Single());
        Clock.UtcNow += TimeSpan.FromDays(1);

        await RunPurgeAsync();

        // Prueba que la limpieza corrió: RunOnceAsync registra los fallos sin propagarlos.
        (await FindUserAsync(ExpiredEmail)).ShouldBeNull();
        var user = await FindUserAsync(Email);
        user.ShouldNotBeNull();
        user.IsEmailConfirmed.ShouldBeTrue();
    }

    private async Task RegisterAsync(string email)
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email, password = Password });
        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    private async Task ConfirmAsync(string token)
    {
        var response = await Client.PostAsJsonAsync(ConfirmUrl, new { token });
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    private Task RunPurgeAsync() =>
        Factory.GetJob<PurgeUnconfirmedAccountsJob>().RunOnceAsync(CancellationToken.None);
}
