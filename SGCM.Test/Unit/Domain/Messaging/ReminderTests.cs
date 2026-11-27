using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Messaging.Reminders;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Messaging;

public sealed class ReminderTests
{
    private const int AppointmentId = 10;
    private const int TemplateId = 1;
    private const int DoctorUserId = 7;
    private const string Message = "Hola Ana, le recordamos su cita de mañana a las 09:00.";
    private static readonly DateTimeOffset Now = new(2027, 1, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SendAt = Now.AddDays(1);

    [Fact]
    public void Programa_un_recordatorio_pendiente_con_el_texto_final_y_la_hora_de_envio_en_UTC()
    {
        var reminder = Schedule(templateId: TemplateId, scheduledAt: SendAt.ToOffset(TimeSpan.FromHours(-4)));

        reminder.AppointmentId.ShouldBe(AppointmentId);
        reminder.TemplateId.ShouldBe(TemplateId);
        reminder.MessageBody.ShouldBe(Message);
        reminder.ScheduledAt.ShouldBe(SendAt);
        reminder.ScheduledAt.Offset.ShouldBe(TimeSpan.Zero);
        reminder.Status.ShouldBe(ReminderStatus.Pending);
        reminder.SentAt.ShouldBeNull();
        reminder.CreatedBy.ShouldBeNull();
    }

    [Fact]
    public void Un_mensaje_libre_no_tiene_plantilla_y_guarda_quien_lo_programo()
    {
        var reminder = Schedule(createdBy: DoctorUserId);

        reminder.TemplateId.ShouldBeNull();
        reminder.CreatedBy.ShouldBe(DoctorUserId);
    }

    [Fact]
    public void Recorta_el_mensaje()
    {
        Schedule(message: $"  {Message}  ").MessageBody.ShouldBe(Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void El_mensaje_es_obligatorio(string? message)
    {
        Reminder.Schedule(AppointmentId, null, message, SendAt, Now).Error.ShouldBe(ReminderErrors.MessageRequired);
    }

    [Fact]
    public void El_mensaje_admite_hasta_2000_caracteres()
    {
        Reminder.Schedule(AppointmentId, null, new string('A', Reminder.MessageMaxLength), SendAt, Now).IsSuccess.ShouldBeTrue();
        Reminder.Schedule(AppointmentId, null, new string('A', Reminder.MessageMaxLength + 1), SendAt, Now).Error
            .ShouldBe(ReminderErrors.MessageTooLong);
    }

    [Fact]
    public void La_hora_de_envio_debe_ser_futura()
    {
        Reminder.Schedule(AppointmentId, null, Message, Now, Now).Error.ShouldBe(ReminderErrors.ScheduledInPast);
        Reminder.Schedule(AppointmentId, null, Message, Now.AddMinutes(-1), Now).Error.ShouldBe(ReminderErrors.ScheduledInPast);
    }

    [Fact]
    public void Identificadores_invalidos_son_un_error_de_programacion()
    {
        Should.Throw<DomainException>(() => Reminder.Schedule(0, null, Message, SendAt, Now));
        Should.Throw<DomainException>(() => Reminder.Schedule(AppointmentId, 0, Message, SendAt, Now));
        Should.Throw<DomainException>(() => Reminder.Schedule(AppointmentId, null, Message, SendAt, Now, createdBy: 0));
    }

    [Fact]
    public void Esta_vencido_solo_si_esta_pendiente_y_llego_la_hora()
    {
        var reminder = Schedule();

        reminder.IsDue(SendAt.AddMinutes(-1)).ShouldBeFalse();
        reminder.IsDue(SendAt).ShouldBeTrue();
        reminder.Cancel();
        reminder.IsDue(SendAt).ShouldBeFalse();
    }

    [Fact]
    public void El_envio_exitoso_lo_marca_enviado_con_la_hora()
    {
        var reminder = Schedule();

        reminder.MarkSent(SendAt.AddSeconds(30)).IsSuccess.ShouldBeTrue();

        reminder.Status.ShouldBe(ReminderStatus.Sent);
        reminder.SentAt.ShouldBe(SendAt.AddSeconds(30));
    }

    [Fact]
    public void El_envio_fallido_lo_marca_fallido_sin_hora_de_envio()
    {
        var reminder = Schedule();

        reminder.MarkFailed().IsSuccess.ShouldBeTrue();

        reminder.Status.ShouldBe(ReminderStatus.Failed);
        reminder.SentAt.ShouldBeNull();
    }

    [Fact]
    public void Anula_un_recordatorio_pendiente()
    {
        var reminder = Schedule();

        reminder.Cancel().IsSuccess.ShouldBeTrue();

        reminder.Status.ShouldBe(ReminderStatus.Cancelled);
    }

    [Fact]
    public void Cambia_la_hora_de_un_recordatorio_pendiente_a_una_hora_futura()
    {
        var reminder = Schedule();

        reminder.Reschedule(SendAt.AddDays(2), Now).IsSuccess.ShouldBeTrue();
        reminder.ScheduledAt.ShouldBe(SendAt.AddDays(2));

        reminder.Reschedule(Now, Now).Error.ShouldBe(ReminderErrors.ScheduledInPast);
        reminder.ScheduledAt.ShouldBe(SendAt.AddDays(2));
    }

    public static IEnumerable<object[]> FinishedStates()
    {
        yield return [(Action<Reminder>)(reminder => reminder.MarkSent(SendAt)), ReminderStatus.Sent];
        yield return [(Action<Reminder>)(reminder => reminder.MarkFailed()), ReminderStatus.Failed];
        yield return [(Action<Reminder>)(reminder => reminder.Cancel()), ReminderStatus.Cancelled];
    }

    [Theory]
    [MemberData(nameof(FinishedStates))]
    public void Un_recordatorio_que_no_esta_pendiente_no_se_envia_ni_se_anula_ni_cambia_de_hora(
        Action<Reminder> finish, ReminderStatus status)
    {
        var reminder = Schedule();
        finish(reminder);
        var sentAt = reminder.SentAt;

        reminder.MarkSent(SendAt.AddHours(1)).Error.ShouldBe(ReminderErrors.NotPending);
        reminder.MarkFailed().Error.ShouldBe(ReminderErrors.NotPending);
        reminder.Cancel().Error.ShouldBe(ReminderErrors.NotPending);
        reminder.Reschedule(SendAt.AddDays(3), Now).Error.ShouldBe(ReminderErrors.NotPending);

        reminder.Status.ShouldBe(status);
        reminder.SentAt.ShouldBe(sentAt);
        reminder.ScheduledAt.ShouldBe(SendAt);
    }

    private static Reminder Schedule(
        int? templateId = null, string message = Message, DateTimeOffset? scheduledAt = null, int? createdBy = null) =>
        Reminder.Schedule(AppointmentId, templateId, message, scheduledAt ?? SendAt, Now, createdBy).Value;
}
