using SGCM.Test.Common.Web;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Estado de un escenario: la app compartida, un cliente propio y la última respuesta. Reqnroll
/// crea una instancia por escenario y la desecha al terminar; la app compartida no se desecha.
/// </summary>
public sealed class ApiScenario : IDisposable
{
    public ApiScenario()
    {
        if (!SharedApp.IsInitialized)
        {
            throw new InvalidOperationException(
                "La app compartida no está inicializada: la inicia el hook [BeforeTestRun] de AcceptanceHooks, " +
                "que debe correr antes de crear el escenario.");
        }

        // Los hooks ya crearon y reiniciaron la app, así que la tarea está completa y no bloquea.
        Factory = SharedApp.GetAsync().GetAwaiter().GetResult();
        Client = Factory.CreateClient();
    }

    public SgcmApplicationFactory Factory { get; }

    public HttpClient Client { get; }

    public HttpResponseMessage? Response { get; set; }

    public void Dispose()
    {
        Response?.Dispose();
        Client.Dispose();
    }
}