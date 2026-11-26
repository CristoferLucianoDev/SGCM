using NSubstitute;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.EventHandlers;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class SendAppointmentConfirmedNotificationTests
{
    private const int AppointmentId = 99;
    private const int HoursBeforeStartInsideDeadline = 72;

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

    public SendAppointmentConfirmedNotificationTests()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data);
        _notifications.SendAsync(Arg.Do<Notification>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task Envia_un_solo_correo_al_paciente_con_los_datos_de_la_cita_y_pasa_el_token()
    {
        using var cancellation = new CancellationTokenSource();

        await new SendAppointmentConfirmedNotification(_reader, _notifications)
            .HandleAsync(new AppointmentConfirmedEvent(Confirmed()), cancellation.Token);

        var notification = _sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentConfirmedNotification>();
        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        notification.Appointment.ShouldBe(Data);
        await _reader.Received(1).GetAsync(AppointmentId, cancellation.Token);
        await _notifications.Received(1).SendAsync(Arg.Any<Notification>(), cancellation.Token);
    }

    [Fact]
    public async Task El_medico_no_recibe_correo()
    {
        await new SendAppointmentConfirmedNotification(_reader, _notifications)
            .HandleAsync(new AppointmentConfirmedEvent(Confirmed()), CancellationToken.None);

        _sent.ShouldNotContain(notification => notification.Recipient.Email == Data.DoctorEmail);
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_no_envia_nada()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);

        await new SendAppointmentConfirmedNotification(_reader, _notifications)
            .HandleAsync(new AppointmentConfirmedEvent(Confirmed()), CancellationToken.None);

        _notifications.ReceivedCalls().ShouldBeEmpty();
    }

    private static Appointment Confirmed()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        appointment.Confirm(
                AppointmentMother.StartsAt.AddHours(-HoursBeforeStartInsideDeadline),
                AppointmentMother.ConfirmationDeadlineHours)
            .IsSuccess.ShouldBeTrue();
        return appointment;
    }
}
