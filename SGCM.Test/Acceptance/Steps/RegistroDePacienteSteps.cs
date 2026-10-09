using System.Net;
using System.Net.Http.Json;
using Reqnroll;
using Shouldly;
using SGCM.Data.Application.Identity.Notifications;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Acceptance.Steps;

[Binding]
public sealed class RegistroDePacienteSteps(ApiScenario scenario)
{
    private const string RegisterUrl = "/api/accounts/register";

    [Given("que existe una cuenta confirmada con el correo {string}")]
    public Task DadoUnaCuentaConfirmada(string email) =>
        scenario.Factory.SaveAsync(UserMother.Confirmed(Email.Create(email).Value));

    [Given("que me registré con el correo {string} y la contraseña {string}")]
    [When("me registro con el correo {string} y la contraseña {string}")]
    public Task MeRegistro(string email, string password) =>
        PostAsync(new { email, password });

    [When("me registro con el correo {string}, la contraseña {string} y el rol {int}")]
    public Task MeRegistroConRol(string email, string password, int role) =>
        PostAsync(new { email, password, role });

    [When("me registro con un correo de {int} caracteres")]
    public Task MeRegistroConCorreoLargo(int length)
    {
        const string domain = "@correo.com";
        var email = new string('a', length - domain.Length) + domain;

        return PostAsync(new { email, password = "Secreta123" });
    }

    [Then("el registro es aceptado")]
    public void ElRegistroEsAceptado() =>
        scenario.Response!.StatusCode.ShouldBe(HttpStatusCode.Accepted);

    [Then("el registro es rechazado con el error {string}")]
    public async Task ElRegistroEsRechazado(string errorCode) =>
        (await scenario.Response!.ReadErrorCodeAsync()).ShouldBe(errorCode);

    [Then("existe una cuenta pendiente de confirmación con rol Paciente para {string}")]
    public async Task ExisteCuentaPendiente(string email)
    {
        var user = await scenario.Factory.FindUserAsync(email);

        user.ShouldNotBeNull();
        user.Role.ShouldBe(UserRole.Patient);
        user.IsEmailConfirmed.ShouldBeFalse();
    }

    [Then("solo el último enlace de confirmación de {string} es válido")]
    public async Task SoloElUltimoEnlaceEsValido(string email)
    {
        var user = await scenario.Factory.FindUserAsync(email);

        user!.Tokens.Count(token => token.InvalidatedAt is null).ShouldBe(1);
    }

    [Then("se enviaron {int} correos de confirmación a {string}")]
    public void SeEnviaronCorreos(int count, string email) =>
        scenario.Factory.Notifications.Sent
            .OfType<AccountConfirmationNotification>()
            .Count(notification => notification.Recipient.Email == email)
            .ShouldBe(count);

    [Then("no se envió ningún correo")]
    public void NoSeEnvioNingunCorreo() =>
        scenario.Factory.Notifications.Sent.ShouldBeEmpty();

    private async Task PostAsync(object body) =>
        scenario.Response = await scenario.Client.PostAsJsonAsync(RegisterUrl, body);
}