using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Shouldly;
using SGCM.Web.Contracts.Identity;

namespace SGCM.Test.Common.Web;

public static class HttpResponseExtensions
{
    /// <summary>
    /// Código estable del error que la API devuelve en la extensión "code" de ProblemDetails.
    /// </summary>
    public static async Task<string?> ReadErrorCodeAsync(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("code").GetString();
    }

    /// <summary>
    /// Verifica el status de la respuesta. A diferencia de <c>StatusCode.ShouldBe</c>, el mensaje
    /// de fallo incluye el cuerpo, donde viene el <c>code</c> del ProblemDetails que explica el rechazo.
    /// </summary>
    /// <param name="response">Respuesta de la API.</param>
    /// <param name="expected">Status esperado.</param>
    /// <returns>Una tarea que termina cuando se hizo la verificación.</returns>
    /// <exception cref="ShouldAssertException">Si el status recibido no es el esperado.</exception>
    public static async Task ShouldHaveStatusAsync(this HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();

        throw new ShouldAssertException(string.Create(
            CultureInfo.InvariantCulture,
            $"Se esperaba el status {(int)expected} {expected}, pero se recibió {(int)response.StatusCode} {response.StatusCode}. Cuerpo de la respuesta: {body}"));
    }

    /// <summary>
    /// Lee el cuerpo de una respuesta exitosa del inicio de sesión.
    /// </summary>
    /// <param name="response">Respuesta del endpoint de inicio de sesión.</param>
    /// <returns>El <see cref="LoginResponse"/> deserializado.</returns>
    /// <exception cref="InvalidOperationException">Si la respuesta vino vacía.</exception>
    public static async Task<LoginResponse> ReadLoginAsync(this HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<LoginResponse>()
        ?? throw new InvalidOperationException("La respuesta del inicio de sesión vino vacía.");
}