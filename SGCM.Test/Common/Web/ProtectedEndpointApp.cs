using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Web;

namespace SGCM.Test.Common.Web;

/// <summary>
/// La app compartida de <see cref="SharedApp"/> más <see cref="ProtectedTestController"/>, para
/// probar el paso pendiente contra otra función real del pipeline. Comparte la base, el reloj y la
/// configuración de la app compartida, así que acepta los tokens que esta emite.
/// </summary>
public static class ProtectedEndpointApp
{
    // La app compartida libera esta al liberarse, porque WithWebHostBuilder la registra como derivada.
    private static readonly Lazy<Task<WebApplicationFactory<Program>>> Factory = new(CreateAsync);

    /// <summary>
    /// Devuelve la app con el endpoint de prueba y la crea si nadie la pidió antes.
    /// </summary>
    /// <returns>La única instancia de la ejecución. No la deseches.</returns>
    public static Task<WebApplicationFactory<Program>> GetAsync() => Factory.Value;

    private static async Task<WebApplicationFactory<Program>> CreateAsync()
    {
        var shared = await SharedApp.GetAsync();

        return shared.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddControllers().AddApplicationPart(typeof(ProtectedTestController).Assembly)));
    }
}
