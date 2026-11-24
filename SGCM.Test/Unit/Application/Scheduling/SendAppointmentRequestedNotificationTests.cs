using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.EventHandlers;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class SendAppointmentRequestedNotificationTests
{
    private const int AppointmentId = 99;

    private static readonly AppointmentNotificationData Data = new(
        AppointmentId,
        "Ana Pérez",
        "paciente@correo.com",
        "Luis Pérez",
        "medico@correo.com",
        "Cardiología",
        1500m,
        new DateOnly(2027, 1, 12),
        new TimeOnly(9, 0),
        new TimeOnly(11, 0),
        "Control anual");

    private readonly IAppointmentNotificationReader _reader = Substitute.For<IAppointmentNotificationReader>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly List<Notification> _sent = [];

    public SendAppointmentRequestedNotificationTests()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data);
        _notifications.SendAsync(Arg.Do<Notification>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task Envia_un_solo_correo_al_medico_con_los_datos_de_la_cita_y_el_plazo_de_confirmacion()
    {
        using var cancellation = new CancellationTokenSource();

        await CreateHandler().HandleAsync(new AppointmentRequestedEvent(Requested()), cancellation.Token);

        var notification = _sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRequestedNotification>();
        notification.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        notification.Appointment.ShouldBe(Data);
        notification.ConfirmationDeadlineHours.ShouldBe(48);
        await _reader.Received(1).GetAsync(AppointmentId, cancellation.Token);
        await _notifications.Received(1).SendAsync(Arg.Any<Notification>(), cancellation.Token);
    }

    [Fact]
    public async Task El_paciente_no_recibe_correo()
    {
        await CreateHandler().HandleAsync(new AppointmentRequestedEvent(Requested()), CancellationToken.None);

        _sent.ShouldNotContain(notification => notification.Recipient.Email == Data.PatientEmail);
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_no_envia_nada()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);

        await CreateHandler().HandleAsync(new AppointmentRequestedEvent(Requested()), CancellationToken.None);

        _notifications.ReceivedCalls().ShouldBeEmpty();
    }

    private static Appointment Requested()
    {
        var appointment = AppointmentMother.Requested();
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        return appointment;
    }

    private SendAppointmentRequestedNotification CreateHandler() => new(
        _reader,
        _notifications,
        Options.Create(new SchedulingOptions { ConfirmationDeadlineHours = 48 }));
}
