using NSubstitute;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Messaging;
using SGCM.Data.Application.Messaging.Commands.SendDueReminders;
using SGCM.Data.Application.Messaging.Notifications;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

public sealed class SendDueRemindersCommandHandlerTests
{
    private const int AppointmentId = 40;
    private const int DoctorUserId = 9;
    private static readonly DateTimeOffset Now = new(2027, 1, 11, 13, 0, 0, TimeSpan.Zero);

    private readonly IReminderRepository _reminders = Substitute.For<IReminderRepository>();
    private readonly IReminderTemplateRepository _templates = Substitute.For<IReminderTemplateRepository>();
    private readonly IAppointmentNotificationReader _appointmentReader = Substitute.For<IAppointmentNotificationReader>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly List<ReminderNotification> _sent = [];

    public SendDueRemindersCommandHandlerTests()
    {
        _appointmentReader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data());
        _templates.GetByIdAsync(ReminderTemplateMother.GlobalId, Arg.Any<CancellationToken>()).Returns(ReminderTemplateMother.Global());
        _notifications.SendAsync(Arg.Do<Notification>(notification => _sent.Add((ReminderNotification)notification)), Arg.Any<CancellationToken>())
            .Returns(Result.Success());
    }

    [Fact]
    public async Task Envia_los_recordatorios_cuya_hora_llego_al_paciente_y_los_marca_enviados()
    {
        var reminder = Due(ReminderTemplateMother.GlobalId, "Hola Ana, mañana es su cita.");
        GivenDue(reminder);

        var result = await Handle();

        result.Value.ShouldBe(1);
        reminder.Status.ShouldBe(ReminderStatus.Sent);
        reminder.SentAt.ShouldBe(Now);
        var notification = _sent.ShouldHaveSingleItem();
        notification.Recipient.ShouldBe(new Recipient("ana@correo.com", "Ana Pérez"));
        notification.Message.ShouldBe("Hola Ana, mañana es su cita.");
        notification.Subject.ShouldBe("Recordatorio: su cita de Cardiología es manana");
    }

    [Fact]
    public async Task Un_mensaje_libre_va_con_un_asunto_fijo_con_la_especialidad_y_la_fecha()
    {
        GivenDue(Due(templateId: null, "Traiga sus estudios.", DoctorUserId));

        await Handle();

        _sent.ShouldHaveSingleItem().Subject.ShouldBe("Recordatorio de su cita de Cardiología del 12/01/2027");
        _templates.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Si_el_envio_falla_lo_marca_fallido_y_sigue_con_los_demas()
    {
        var failing = Due(ReminderTemplateMother.GlobalId, "Primero");
        var next = Due(ReminderTemplateMother.GlobalId, "Segundo");
        GivenDue(failing, next);
        _notifications.SendAsync(Arg.Is<Notification>(value => ((ReminderNotification)value).Message == "Primero"), Arg.Any<CancellationToken>())
            .Returns(Result.Failure(Error.Failure("Notification.DeliveryFailed", "SMTP caído")));

        var result = await Handle();

        result.Value.ShouldBe(2);
        failing.Status.ShouldBe(ReminderStatus.Failed);
        failing.SentAt.ShouldBeNull();
        next.Status.ShouldBe(ReminderStatus.Sent);
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_lo_marca_fallido_sin_enviar()
    {
        var reminder = Due(ReminderTemplateMother.GlobalId, "Texto");
        GivenDue(reminder);
        _appointmentReader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);

        (await Handle()).Value.ShouldBe(1);

        reminder.Status.ShouldBe(ReminderStatus.Failed);
        await _notifications.DidNotReceive().SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Es_idempotente_un_recordatorio_que_ya_no_esta_pendiente_no_se_reenvia()
    {
        var alreadySent = Due(ReminderTemplateMother.GlobalId, "Texto");
        alreadySent.MarkSent(Now.AddMinutes(-1));
        var cancelled = Due(ReminderTemplateMother.GlobalId, "Texto");
        cancelled.Cancel();
        GivenDue(alreadySent, cancelled);

        (await Handle()).Value.ShouldBe(0);

        await _notifications.DidNotReceive().SendAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pide_al_repositorio_los_vencidos_a_la_hora_actual_en_lotes()
    {
        GivenDue();

        (await Handle()).Value.ShouldBe(0);

        await _reminders.Received(1).ListDueAsync(Now, Arg.Is<int>(limit => limit > 0), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Lee_cada_plantilla_una_sola_vez_por_lote()
    {
        GivenDue(Due(ReminderTemplateMother.GlobalId, "Uno"), Due(ReminderTemplateMother.GlobalId, "Dos"));

        await Handle();

        await _templates.Received(1).GetByIdAsync(ReminderTemplateMother.GlobalId, Arg.Any<CancellationToken>());
        _sent.Count.ShouldBe(2);
    }

    private Task<Result<int>> Handle() =>
        new SendDueRemindersCommandHandler(
                _reminders, _templates, _appointmentReader, new ReminderComposer(new TemplateRenderer()), _notifications, _clock)
            .HandleAsync(new SendDueRemindersCommand(), CancellationToken.None);

    private void GivenDue(params Reminder[] reminders) =>
        _reminders.ListDueAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)reminders.ToList());

    // Se programa en el pasado respecto al reloj del lote: llegó su hora.
    private static Reminder Due(int? templateId, string message, int? createdBy = null) =>
        Reminder.Schedule(AppointmentId, templateId, message, Now.AddMinutes(-5), Now.AddHours(-1), createdBy).Value;

    private static AppointmentNotificationData Data() => new(
        AppointmentId, "Ana Pérez", "ana@correo.com", "Luis Gómez", "luis@correo.com", "Cardiología", 2500m,
        new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0), null);
}
