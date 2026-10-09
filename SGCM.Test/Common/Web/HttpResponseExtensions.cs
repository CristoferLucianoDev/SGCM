using System.Net.Http.Json;
using System.Text.Json;

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
}