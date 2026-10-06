using Microsoft.Extensions.Options;
using SGCM.Data.Infrastructure.Notifications.Channels;

namespace SGCM.Test.Integration.Notifications;

[Trait("Category", "Integration")]
public class SmtpEmailTransportTests
{
    [Fact]
    public async Task SendAsync_entrega_el_correo_al_buzon_local_de_Supabase()
    {
        var options = Options.Create(new SmtpOptions
        {
            Host = "localhost",
            Port = 54325,
            UseStartTls = false,
            SenderEmail = "no-reply@sgcm.local",
            SenderName = "Sistema de Gestión de Citas Médicas"
        });
        var transport = new SmtpEmailTransport(options);
        var subject = $"Prueba de integración {Guid.NewGuid():N}";

        var exception = await Record.ExceptionAsync(() => transport.SendAsync(
            "paciente@correo.com",
            "Paciente de prueba",
            subject,
            "<p>Correo de prueba de <strong>SGCM</strong>.</p>",
            CancellationToken.None));

        Assert.Null(exception);
    }
}