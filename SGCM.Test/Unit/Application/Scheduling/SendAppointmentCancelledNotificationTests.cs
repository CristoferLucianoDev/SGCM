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

public sealed class SendAppointmentCancelledNotificationTests
{
    private const int AppointmentId = 99;
    private const string Reason = "Viaje";
    private const int HoursBeforeStartInsideDeadline = 48;

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

    public SendAppointmentCancelledNotificationTests()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data);
        _notifications.SendAsync(Arg.Do<Notification>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Result.Success());
    }

    [Fact]
    public async Task Envia_un_solo_correo_al_medico_con_los_datos_de_la_cita_y_el_motivo()
    {
        using var cancellation = new CancellationTokenSource();

        await new SendAppointmentCancelledNotification(_reader, _notifications)
            .HandleAsync(new AppointmentCancelledEvent(Cancelled(), AppointmentCanceller.Patient), cancellation.Token);

        var notification = _sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCancelledNotification>();
        notification.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        notification.Appointment.ShouldBe(Data);
        notification.CancelledBy.ShouldBe(AppointmentCanceller.Patient);
        notification.CancellationReason.ShouldBe(Reason);
        await _reader.Received(1).GetAsync(AppointmentId, cancellation.Token);
        await _notifications.Received(1).SendAsync(Arg.Any<Notification>(), cancellation.Token);
    }

    [Fact]
    public async Task El_paciente_no_recibe_correo()
    {
        await new SendAppointmentCancelledNotification(_reader, _notifications)
            .HandleAsync(new AppointmentCancelledEvent(Cancelled(), AppointmentCanceller.Patient), CancellationToken.None);

        _sent.ShouldNotContain(notification => notification.Recipient.Email == Data.PatientEmail);
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_no_envia_nada()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);

        await new SendAppointmentCancelledNotification(_reader, _notifications)
            .HandleAsync(new AppointmentCancelledEvent(Cancelled(), AppointmentCanceller.Patient), CancellationToken.None);

        _notifications.ReceivedCalls().ShouldBeEmpty();
    }

    private static Appointment Cancelled()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        appointment.Cancel(
                AppointmentCanceller.Patient,
                Reason,
                AppointmentMother.StartsAt.AddHours(-HoursBeforeStartInsideDeadline),
                AppointmentMother.PatientCancellationDeadlineHours)
            .IsSuccess.ShouldBeTrue();
        return appointment;
    }
}
