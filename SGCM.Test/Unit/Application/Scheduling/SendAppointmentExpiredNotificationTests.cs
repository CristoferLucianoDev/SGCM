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

public sealed class SendAppointmentExpiredNotificationTests
{
    private const int AppointmentId = 99;
    private const int HoursBeforeStartPastDeadline = 47;

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
    private readonly SendAppointmentExpiredNotification _handler;

    public SendAppointmentExpiredNotificationTests()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data);
        _notifications.SendAsync(Arg.Do<Notification>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Result.Success());
        var options = Options.Create(new SchedulingOptions
        {
            ConfirmationDeadlineHours = AppointmentMother.ConfirmationDeadlineHours
        });
        _handler = new SendAppointmentExpiredNotification(_reader, _notifications, options);
    }

    [Fact]
    public async Task Envia_dos_correos_primero_al_paciente_y_luego_al_medico_con_los_datos_y_las_horas()
    {
        using var cancellation = new CancellationTokenSource();

        await _handler.HandleAsync(new AppointmentExpiredEvent(Expired()), cancellation.Token);

        _sent.Count.ShouldBe(2);
        var toPatient = _sent[0].ShouldBeOfType<AppointmentExpiredNotification>();
        toPatient.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        toPatient.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForPatientTemplateKey);
        var toDoctor = _sent[1].ShouldBeOfType<AppointmentExpiredNotification>();
        toDoctor.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        toDoctor.TemplateKey.ShouldBe(AppointmentExpiredNotification.ForDoctorTemplateKey);
        foreach (var notification in new[] { toPatient, toDoctor })
        {
            notification.Appointment.ShouldBe(Data);
            notification.ConfirmationDeadlineHours.ShouldBe(AppointmentMother.ConfirmationDeadlineHours);
        }

        await _reader.Received(1).GetAsync(AppointmentId, cancellation.Token);
        await _notifications.Received(2).SendAsync(Arg.Any<Notification>(), cancellation.Token);
    }

    [Fact]
    public async Task Si_el_primer_envio_falla_igual_envia_el_segundo()
    {
        Result failure = Error.Validation("Notification.SendFailed", "No se pudo enviar el correo.");
        // Sin Arg.Do: _sent ya lo llena el Arg.Do del constructor, y repetirlo registraría cada envío dos veces.
        _notifications.SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>())
            .Returns(failure, Result.Success());

        await _handler.HandleAsync(new AppointmentExpiredEvent(Expired()), CancellationToken.None);

        _sent.Count.ShouldBe(2);
        _sent[1].Recipient.Email.ShouldBe(Data.DoctorEmail);
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_no_envia_nada()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);

        await _handler.HandleAsync(new AppointmentExpiredEvent(Expired()), CancellationToken.None);

        _notifications.ReceivedCalls().ShouldBeEmpty();
    }

    private static Appointment Expired()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        appointment.Expire(
                AppointmentMother.StartsAt.AddHours(-HoursBeforeStartPastDeadline),
                AppointmentMother.ConfirmationDeadlineHours)
            .IsSuccess.ShouldBeTrue();
        return appointment;
    }
}
