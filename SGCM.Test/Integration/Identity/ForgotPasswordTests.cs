using System.Globalization;
using System.Net;
using Shouldly;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class ForgotPasswordTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Email = "paciente@correo.com";
    private const string PendingEmail = "pendiente@correo.com";
    private const string InactiveEmail = "inactivo@correo.com";
    private const string UnknownEmail = "desconocido@correo.com";
    private const string InvalidEmailFormat = "no-es-un-correo";
    private const string Password = "Secreta123";
    private const string NewPassword = "Renovada123";
    private const int LowIpLimit = 3;

    // El limitador por IP usa el reloj real, no Clock: una ventana de un día evita que se
    // renueve a mitad de la prueba en una máquina lenta.
    private const int LongIpWindowSeconds = 24 * 60 * 60;

    private const string UnknownEmailCase = "correo_inexistente";
    private const string PendingAccountCase = "cuenta_pendiente";
    private const string InactiveAccountCase = "cuenta_inactiva";

    // Límite de solicitudes por correo según el PBI.
    private const int MaxRequestsPerEmail = 3;
    private static readonly TimeSpan EmailWindow = TimeSpan.FromMinutes(15);

    // Separa las dos emisiones en el tiempo, como pasaría en un uso real, sin acercarse al
    // vencimiento del primer enlace: si falla, es porque fue reemplazado y no porque venció.
    private static readonly TimeSpan TimeBetweenRequests = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Una_cuenta_confirmada_recibe_el_enlace()
    {
        await CreateConfirmedPatientAsync(Email, Password);

        var response = await ForgotPasswordAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var notification = Factory.Notifications.Sent
            .OfType<PasswordResetNotification>()
            .ShouldHaveSingleItem();
        notification.Recipient.Email.ShouldBe(Email);
        notification.ResetUrl.ShouldStartWith(SgcmApplicationFactory.PasswordResetUrl);
    }

    [Fact]
    public async Task La_respuesta_es_identica_exista_o_no_la_cuenta()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        var expected = await ReadSnapshotAsync(await ForgotPasswordAsync(Email));

        foreach (var caso in new[] { UnknownEmailCase, PendingAccountCase, InactiveAccountCase })
        {
            var email = await PrepareAccountWithoutLinkAsync(caso);

            var actual = await ReadSnapshotAsync(await ForgotPasswordAsync(email));

            actual.ShouldBe(expected, caso);
        }
    }

    [Theory]
    [InlineData(UnknownEmailCase)]
    [InlineData(PendingAccountCase)]
    [InlineData(InactiveAccountCase)]
    public async Task Correo_inexistente_pendiente_o_inactivo_no_recibe_enlace(string caso)
    {
        var email = await PrepareAccountWithoutLinkAsync(caso);

        var response = await ForgotPasswordAsync(email);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        Factory.PasswordResetTokensFor(email).ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_correo_invalido_responde_400()
    {
        var response = await ForgotPasswordAsync(InvalidEmailFormat);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public async Task Una_solicitud_nueva_reemplaza_el_enlace_anterior()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await (await ForgotPasswordAsync(Email)).ShouldHaveStatusAsync(HttpStatusCode.Accepted);
        Clock.UtcNow += TimeBetweenRequests;
        await (await ForgotPasswordAsync(Email)).ShouldHaveStatusAsync(HttpStatusCode.Accepted);
        var tokens = Factory.PasswordResetTokensFor(Email);

        var previous = await ResetPasswordAsync(tokens[0], NewPassword);

        previous.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await previous.ReadErrorCodeAsync()).ShouldBe(UserErrors.TokenInvalid.Code);

        var latest = await ResetPasswordAsync(tokens[^1], NewPassword);

        latest.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Superado_el_limite_por_correo_responde_429()
    {
        await CreateConfirmedPatientAsync(Email, Password);
        await RequestWithinLimitAsync(Email, MaxRequestsPerEmail);

        var rejected = await ForgotPasswordAsync(Email);

        rejected.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await rejected.ReadErrorCodeAsync()).ShouldBe(UserErrors.TooManyPasswordResetRequests.Code);

        Clock.UtcNow += EmailWindow;

        var response = await ForgotPasswordAsync(Email);

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task El_limite_por_correo_cuenta_tambien_los_correos_inexistentes()
    {
        // Así el límite no revela qué correos existen: un correo sin cuenta se bloquea igual.
        await RequestWithinLimitAsync(UnknownEmail, MaxRequestsPerEmail);

        var response = await ForgotPasswordAsync(UnknownEmail);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.TooManyPasswordResetRequests.Code);
    }

    [Fact]
    public async Task Superado_el_limite_por_IP_responde_429()
    {
        await using var factory = new SgcmApplicationFactory(
            Database.ConnectionString,
            new Dictionary<string, string?>
            {
                ["PasswordResetLimits:MaxRequestsPerIp"] = LowIpLimit.ToString(CultureInfo.InvariantCulture),
                ["PasswordResetLimits:IpWindowSeconds"] = LongIpWindowSeconds.ToString(CultureInfo.InvariantCulture)
            });
        using var client = factory.CreateClient();

        // Un correo distinto por solicitud, para que el límite que corte sea el de IP y no el de correo.
        for (var attempt = 0; attempt < LowIpLimit; attempt++)
        {
            await (await ForgotPasswordAsync(DistinctEmail(attempt), client)).ShouldHaveStatusAsync(HttpStatusCode.Accepted);
        }

        var response = await ForgotPasswordAsync(DistinctEmail(LowIpLimit), client);

        response.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        (await response.ReadErrorCodeAsync()).ShouldBe(CommonErrors.TooManyRequests.Code);
        response.Headers.RetryAfter.ShouldNotBeNull();
    }

    private async Task<string> PrepareAccountWithoutLinkAsync(string caso)
    {
        switch (caso)
        {
            case UnknownEmailCase:
                return UnknownEmail;
            case PendingAccountCase:
                await RegisterPatientAsync(PendingEmail, Password);
                return PendingEmail;
            case InactiveAccountCase:
                await CreateConfirmedPatientAsync(InactiveEmail, Password);
                await Factory.UpdateUserAsync(InactiveEmail, user => user.Deactivate().IsSuccess.ShouldBeTrue());
                return InactiveEmail;
            default:
                throw new ArgumentOutOfRangeException(nameof(caso), caso, "Caso de prueba desconocido.");
        }
    }

    private async Task RequestWithinLimitAsync(string email, int requests)
    {
        for (var request = 0; request < requests; request++)
        {
            await (await ForgotPasswordAsync(email)).ShouldHaveStatusAsync(HttpStatusCode.Accepted);
        }
    }

    private static string DistinctEmail(int index) =>
        string.Create(CultureInfo.InvariantCulture, $"desconocido{index}@correo.com");

    // Lo que un cliente puede observar de la respuesta: si algo de esto cambia según la
    // cuenta, la API revelaría qué correos están registrados.
    private static async Task<ResponseSnapshot> ReadSnapshotAsync(HttpResponseMessage response) =>
        new(response.StatusCode, response.Content.Headers.ContentType?.ToString(), await response.Content.ReadAsStringAsync());

    private sealed record ResponseSnapshot(HttpStatusCode StatusCode, string? ContentType, string Body);
}
