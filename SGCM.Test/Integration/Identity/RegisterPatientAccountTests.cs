using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Shouldly;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Identity;

[Trait("Category", "Integration")]
public sealed class RegisterPatientAccountTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string RegisterUrl = "/api/accounts/register";
    private const string Email = "paciente@correo.com";
    private const string Password = "Secreta123";

    [Fact]
    public async Task Registro_con_correo_nuevo_crea_paciente_pendiente_y_envia_confirmacion()
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email = Email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var user = await FindUserAsync(Email);
        user.ShouldNotBeNull();
        user.Role.ShouldBe(UserRole.Patient);
        user.IsEmailConfirmed.ShouldBeFalse();
        user.PasswordHash.ShouldNotBe(Password);

        var notification = Factory.Notifications.Sent.OfType<AccountConfirmationNotification>().ShouldHaveSingleItem();
        notification.Recipient.Email.ShouldBe(Email);
        notification.ConfirmationUrl.ShouldStartWith(SgcmApplicationFactory.ConfirmationUrl);

        // El token del enlace es el que corresponde al hash guardado.
        var token = user.Tokens.ShouldHaveSingleItem();
        token.TokenHash.ShouldBe(HashOf(TokenFrom(notification.ConfirmationUrl)));
    }

    [Fact]
    public async Task Registro_con_correo_de_cuenta_confirmada_se_rechaza()
    {
        await SaveAsync(UserMother.Confirmed());

        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email = UserMother.DefaultEmail.Value, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await response.ReadErrorCodeAsync()).ShouldBe(UserErrors.EmailAlreadyInUse.Code);
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Registro_con_correo_pendiente_reemplaza_contrasena_e_invalida_el_token_anterior()
    {
        await Client.PostAsJsonAsync(RegisterUrl, new { email = Email, password = Password });
        var first = await FindUserAsync(Email);

        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email = Email, password = "OtraClave456" });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);

        var user = await FindUserAsync(Email);
        user!.Id.ShouldBe(first!.Id);
        user.PasswordHash.ShouldNotBe(first.PasswordHash);
        user.Tokens.Count.ShouldBe(2);
        user.Tokens.Count(t => t.InvalidatedAt is null).ShouldBe(1);

        var notifications = Factory.Notifications.Sent.OfType<AccountConfirmationNotification>().ToList();
        notifications.Count.ShouldBe(2);
        notifications[0].ConfirmationUrl.ShouldNotBe(notifications[1].ConfirmationUrl);
    }

    [Theory]
    [InlineData("no-es-un-correo", Password, "Common.InvalidEmail")]
    [InlineData("paciente@correo.com", "corta", "User.PasswordTooShort")]
    public async Task Registro_con_datos_invalidos_se_rechaza_indicando_el_dato(string email, string password, string expectedCode)
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email, password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(expectedCode);
        Factory.Notifications.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task Registro_con_correo_de_mas_de_256_caracteres_se_rechaza()
    {
        var email = new string('a', 246) + "@correo.com";

        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email, password = Password });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadErrorCodeAsync()).ShouldBe(CommonErrors.InvalidEmail.Code);
    }

    [Fact]
    public async Task Registro_que_indica_otro_rol_crea_igualmente_un_paciente()
    {
        var response = await Client.PostAsJsonAsync(RegisterUrl, new { email = Email, password = Password, role = 3 });

        response.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await FindUserAsync(Email))!.Role.ShouldBe(UserRole.Patient);
    }

    private static string TokenFrom(string link) =>
        QueryHelpers.ParseQuery(new Uri(link).Query)["token"].ToString();

    private static string HashOf(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}