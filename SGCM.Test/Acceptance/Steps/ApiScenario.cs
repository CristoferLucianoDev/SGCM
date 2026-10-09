using SGCM.Test.Common.Web;

namespace SGCM.Test.Acceptance.Steps;

/// <summary>
/// Estado de un escenario: la app en memoria y la última respuesta. Reqnroll crea
/// una instancia por escenario y la desecha al terminar.
/// </summary>
public sealed class ApiScenario : IDisposable
{
    public ApiScenario()
    {
        Factory = new SgcmApplicationFactory(AcceptanceHooks.Database.ConnectionString);
        Client = Factory.CreateClient();
    }

    public SgcmApplicationFactory Factory { get; }

    public HttpClient Client { get; }

    public HttpResponseMessage? Response { get; set; }

    public void Dispose()
    {
        Response?.Dispose();
        Client.Dispose();
        Factory.Dispose();
    }
}