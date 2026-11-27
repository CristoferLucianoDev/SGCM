using NSubstitute;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling.EventHandlers;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class SendAppointmentRescheduledNotificationTests
{
    private const int AppointmentId = 99;
    private const int ThreeDaysHours = 72;
    private const string SantoDomingoZone = "America/Santo_Domingo";

    private static readonly AppointmentNotificationData Data = new(
        AppointmentId,
        "Ana Pérez",
        "paciente@correo.com",
        "Luis Pérez",
        "medico@correo.com",
        "Cardiología",
        1500m,
        new DateOnly(2027, 1, 20),
        new TimeOnly(10, 0),
        new TimeOnly(12, 0),
        "Control anual");

    // Miércoles 20 de enero de 2027 a las 10:00 en Santo Domingo.
    private static readonly DateTimeOffset NewStartsAt = new(2027, 1, 20, 10, 0, 0, AppointmentMother.SantoDomingo);

    private readonly IAppointmentNotificationReader _reader = Substitute.For<IAppointmentNotificationReader>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly List<Notification> _sent = [];
    private readonly SendAppointmentRescheduledNotification _handler;

    public SendAppointmentRescheduledNotificationTests()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data);
        _notifications.SendAsync(Arg.Do<Notification>(_sent.Add), Arg.Any<CancellationToken>()).Returns(Result.Success());
        _handler = new SendAppointmentRescheduledNotification(
            _reader, _notifications, BusinessCalendar.ForTimeZone(SantoDomingoZone));
    }

    [Fact]
    public async Task Envia_un_solo_correo_al_medico_con_los_datos_leidos_y_el_horario_anterior_en_hora_local()
    {
        var (appointment, domainEvent) = RescheduledByPatient();
        using var cancellation = new CancellationTokenSource();

        await _handler.HandleAsync(domainEvent, cancellation.Token);

        var notification = _sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledNotification>();
        notification.Recipient.ShouldBe(new Recipient("medico@correo.com", "Luis Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByPatientTemplateKey);
        notification.Appointment.ShouldBe(Data);
        notification.RescheduledBy.ShouldBe(AppointmentParticipant.Patient);
        // 13:00 UTC del martes 12 de enero es la 09:00 en Santo Domingo.
        notification.PreviousDate.ShouldBe(new DateOnly(2027, 1, 12));
        notification.PreviousStartTime.ShouldBe(new TimeOnly(9, 0));
        appointment.StartsAt.ShouldBe(NewStartsAt);
        await _reader.Received(1).GetAsync(AppointmentId, cancellation.Token);
        await _notifications.Received(1).SendAsync(Arg.Any<Notification>(), cancellation.Token);
    }

    [Fact]
    public async Task El_paciente_no_recibe_correo()
    {
        var (_, domainEvent) = RescheduledByPatient();

        await _handler.HandleAsync(domainEvent, CancellationToken.None);

        _sent.ShouldNotContain(notification => notification.Recipient.Email == Data.PatientEmail);
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_no_envia_nada()
    {
        _reader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);
        var (_, domainEvent) = RescheduledByPatient();

        await _handler.HandleAsync(domainEvent, CancellationToken.None);

        _notifications.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_horario_anterior_cerca_de_la_medianoche_UTC_cae_en_el_dia_local_anterior()
    {
        var appointment = WithId();
        // 02:00 UTC del 13 de enero es las 22:00 del 12 de enero en Santo Domingo.
        var previousStartsAt = new DateTimeOffset(2027, 1, 13, 2, 0, 0, TimeSpan.Zero);

        await _handler.HandleAsync(
            new AppointmentRescheduledEvent(appointment, AppointmentParticipant.Patient, previousStartsAt),
            CancellationToken.None);

        var notification = _sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledNotification>();
        notification.PreviousDate.ShouldBe(new DateOnly(2027, 1, 12));
        notification.PreviousStartTime.ShouldBe(new TimeOnly(22, 0));
    }

    [Fact]
    public async Task Si_reprograma_el_medico_envia_un_solo_correo_al_paciente_con_la_plantilla_del_medico()
    {
        var appointment = WithId();
        var domainEvent = new AppointmentRescheduledEvent(
            appointment, AppointmentParticipant.Doctor, AppointmentMother.StartsAt);

        await _handler.HandleAsync(domainEvent, CancellationToken.None);

        var notification = _sent.ShouldHaveSingleItem().ShouldBeOfType<AppointmentRescheduledNotification>();
        notification.Recipient.ShouldBe(new Recipient("paciente@correo.com", "Ana Pérez"));
        notification.TemplateKey.ShouldBe(AppointmentRescheduledNotification.RescheduledByDoctorTemplateKey);
        notification.RescheduledBy.ShouldBe(AppointmentParticipant.Doctor);
        _sent.ShouldNotContain(sent => sent.Recipient.Email == Data.DoctorEmail);
    }

    private static Appointment WithId()
    {
        var appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        return appointment;
    }

    // Reprograma la cita del paciente dentro de plazo y devuelve el evento que emite el agregado.
    private static (Appointment Appointment, AppointmentRescheduledEvent Event) RescheduledByPatient()
    {
        var appointment = WithId();
        appointment.RescheduleByPatient(
                NewStartsAt,
                AppointmentMother.StartsAt.AddHours(-ThreeDaysHours),
                AppointmentMother.PatientRescheduleDeadlineHours,
                AppointmentMother.DurationHours,
                AppointmentMother.BookingWindow)
            .IsSuccess.ShouldBeTrue();

        return (appointment, appointment.DomainEvents.OfType<AppointmentRescheduledEvent>().Single());
    }
}
