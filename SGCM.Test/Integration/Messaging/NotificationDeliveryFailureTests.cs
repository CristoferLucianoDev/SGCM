using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Serilog.Core;
using Serilog.Events;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Data.Infrastructure.Notifications.Channels;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;
using SGCM.Test.Integration.Scheduling;
using Shouldly;

namespace SGCM.Test.Integration.Messaging;

/// <summary>
/// Fallo del servicio de correo con el NotificationService real y un transporte que siempre falla: el cambio de la
/// cita se conserva y el fallo queda registrado en el log.
/// </summary>
[Trait("Category", "Integration")]
public sealed class NotificationDeliveryFailureTests(PostgresFixture database) : AppointmentCancellationTestBase(database)
{
    private const string Url = "/api/doctors/me/appointments";

    private readonly FailingTransport _transport = new();
    private readonly LogProbe _log = new();

    [Fact]
    public async Task Al_confirmar_la_cita_queda_confirmada_aunque_el_correo_falle_y_el_fallo_queda_en_el_log()
    {
        var (doctor, patient, appointmentId) = await SeedOwnAppointmentAsync(Pending, InTimeStart());
        using var app = AppWithFailingMail();
        using var client = ClientFor(app, doctor.Client);

        using var response = await client.PostAsync($"{Url}/{appointmentId}/confirm", content: null);

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).Status.ShouldBe(Confirmed);
        _transport.Attempts.ShouldHaveSingleItem().ShouldBe(patient.Email);
        AssertDeliveryFailureLogged("appointment-confirmed");
    }

    [Fact]
    public async Task Al_cancelar_la_cita_queda_cancelada_con_su_motivo_aunque_el_correo_falle()
    {
        var (doctor, _, appointmentId) = await SeedOwnAppointmentAsync(Confirmed, InTimeStart());
        using var app = AppWithFailingMail();
        using var client = ClientFor(app, doctor.Client);

        using var response = await client.PostAsJsonAsync($"{Url}/{appointmentId}/cancel", new { reason = "Viaje al congreso" });

        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);
        (await ReadStateAsync(appointmentId)).ShouldBe(new AppointmentState("cancelled", "Viaje al congreso"));
        AssertDeliveryFailureLogged("appointment-cancelled-for-patient");
    }

    // La misma aplicación con el NotificationService real (la fábrica de pruebas lo reemplaza por el falso), un
    // transporte que siempre falla y el log capturado.
    private WebApplicationFactory<SGCM.Web.Program> AppWithFailingMail() =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.RemoveAll<IEmailTransport>();
            services.AddSingleton<IEmailTransport>(_transport);
            services.AddSingleton<ILogEventSink>(_log);
        }));

    // La sesión vive en la base y el token lo firma la misma configuración, así que sirve en la otra aplicación.
    private static HttpClient ClientFor(WebApplicationFactory<SGCM.Web.Program> app, HttpClient loggedIn)
    {
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = loggedIn.DefaultRequestHeaders.Authorization;
        foreach (var header in loggedIn.DefaultRequestHeaders.UserAgent) client.DefaultRequestHeaders.UserAgent.Add(header);
        return client;
    }

    private void AssertDeliveryFailureLogged(string templateKey)
    {
        var entry = _log.Entries.Where(entry => entry.Level == LogEventLevel.Error
            && entry.Properties.TryGetValue("TemplateKey", out var key)
            && key is ScalarValue { Value: string value } && value == templateKey).ShouldHaveSingleItem();
        entry.Properties["Channel"].ShouldBe(new ScalarValue(nameof(EmailNotificationChannel)));
        entry.Properties["Error"].ShouldBe(new ScalarValue(FailingTransport.Message));
    }

    private sealed class FailingTransport : IEmailTransport
    {
        public const string Message = "El servidor SMTP no responde.";

        public ConcurrentQueue<string> Attempts { get; } = new();

        public Task SendAsync(string toEmail, string toName, string subject, string htmlBody, CancellationToken cancellationToken)
        {
            Attempts.Enqueue(toEmail);
            throw new InvalidOperationException(Message);
        }
    }

    private sealed class LogProbe : ILogEventSink
    {
        public ConcurrentQueue<LogEvent> Entries { get; } = new();

        public void Emit(LogEvent logEvent) => Entries.Enqueue(logEvent);
    }
}
