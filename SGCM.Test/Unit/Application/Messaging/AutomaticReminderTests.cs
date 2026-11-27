using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Messaging;
using SGCM.Data.Application.Messaging.EventHandlers;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

/// <summary>
/// Manejadores del recordatorio automático: se programa 24 horas antes al confirmarse la cita, se anula al cancelarse
/// o vencer y se recalcula al reprogramarse.
/// </summary>
public sealed class AutomaticReminderTests
{
    private const int AppointmentId = 40;
    private const int LeadHours = 24;
    private const int DoctorUserId = 9;

    private readonly IReminderRepository _reminders = Substitute.For<IReminderRepository>();
    private readonly IReminderTemplateRepository _templates = Substitute.For<IReminderTemplateRepository>();
    private readonly IAppointmentNotificationReader _appointmentReader = Substitute.For<IAppointmentNotificationReader>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly List<Reminder> _added = [];
    private readonly Appointment _confirmed = WithId(AppointmentMother.InStatus(AppointmentStatus.Confirmed));

    public AutomaticReminderTests()
    {
        // Dos días antes del inicio: el recordatorio de 24 horas todavía cabe.
        _clock.UtcNow = _confirmed.StartsAt.AddHours(-48);
        _templates.GetActiveGlobalByNameAsync(ReminderTemplate.AutomaticReminderName, Arg.Any<CancellationToken>())
            .Returns(ReminderTemplateMother.Global());
        _appointmentReader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data());
        _reminders.When(repository => repository.Add(Arg.Any<Reminder>())).Do(call => _added.Add(call.Arg<Reminder>()));
        GivenPending();
    }

    [Fact]
    public async Task Al_confirmarse_programa_el_recordatorio_24_horas_antes_con_la_plantilla_global()
    {
        await Confirmed().HandleAsync(new AppointmentConfirmedEvent(_confirmed), CancellationToken.None);

        var reminder = _added.ShouldHaveSingleItem();
        reminder.AppointmentId.ShouldBe(AppointmentId);
        reminder.TemplateId.ShouldBe(ReminderTemplateMother.GlobalId);
        reminder.ScheduledAt.ShouldBe(_confirmed.StartsAt.AddHours(-LeadHours));
        reminder.MessageBody.ShouldBe(
            "Hola Ana Pérez, le recordamos su cita de Cardiología con Luis Gómez el 12/01/2027 a las 09:00.");
        reminder.Status.ShouldBe(ReminderStatus.Pending);
        reminder.CreatedBy.ShouldBeNull();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(LeadHours)]
    [InlineData(LeadHours - 1)]
    [InlineData(1)]
    public async Task Si_faltan_24_horas_o_menos_no_se_programa(int hoursBeforeStart)
    {
        _clock.UtcNow = _confirmed.StartsAt.AddHours(-hoursBeforeStart);

        await Confirmed().HandleAsync(new AppointmentConfirmedEvent(_confirmed), CancellationToken.None);

        _added.ShouldBeEmpty();
        _templates.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_la_plantilla_global_falla_sin_guardar_para_que_el_despachador_lo_registre()
    {
        _templates.GetActiveGlobalByNameAsync(ReminderTemplate.AutomaticReminderName, Arg.Any<CancellationToken>())
            .Returns((ReminderTemplate?)null);

        await Should.ThrowAsync<InvalidOperationException>(
            () => Confirmed().HandleAsync(new AppointmentConfirmedEvent(_confirmed), CancellationToken.None));

        _added.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_la_cita_ya_no_existe_no_programa_nada()
    {
        _appointmentReader.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((AppointmentNotificationData?)null);

        await Confirmed().HandleAsync(new AppointmentConfirmedEvent(_confirmed), CancellationToken.None);

        _added.ShouldBeEmpty();
    }

    [Fact]
    public async Task Al_cancelarse_anula_todos_sus_recordatorios_pendientes()
    {
        var automatic = Pending();
        var byDoctor = Pending(DoctorUserId);
        GivenPending(automatic, byDoctor);

        await new CancelRemindersOnAppointmentCancelled(Scheduler())
            .HandleAsync(new AppointmentCancelledEvent(_confirmed, AppointmentCanceller.Patient), CancellationToken.None);

        automatic.Status.ShouldBe(ReminderStatus.Cancelled);
        byDoctor.Status.ShouldBe(ReminderStatus.Cancelled);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Al_vencer_anula_sus_recordatorios_pendientes()
    {
        var pending = Pending();
        GivenPending(pending);

        await new CancelRemindersOnAppointmentExpired(Scheduler())
            .HandleAsync(new AppointmentExpiredEvent(_confirmed), CancellationToken.None);

        pending.Status.ShouldBe(ReminderStatus.Cancelled);
    }

    [Fact]
    public async Task Sin_recordatorios_pendientes_no_guarda_nada()
    {
        await new CancelRemindersOnAppointmentCancelled(Scheduler())
            .HandleAsync(new AppointmentCancelledEvent(_confirmed, AppointmentCanceller.Doctor), CancellationToken.None);

        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_el_medico_la_reprograma_y_queda_confirmada_recalcula_el_automatico_y_conserva_los_del_medico()
    {
        var automatic = Pending();
        var byDoctor = Pending(DoctorUserId);
        GivenPending(automatic, byDoctor);

        await Rescheduled().HandleAsync(
            new AppointmentRescheduledEvent(_confirmed, AppointmentParticipant.Doctor, _confirmed.StartsAt.AddDays(-1)),
            CancellationToken.None);

        automatic.Status.ShouldBe(ReminderStatus.Cancelled);
        byDoctor.Status.ShouldBe(ReminderStatus.Pending);
        _added.ShouldHaveSingleItem().ScheduledAt.ShouldBe(_confirmed.StartsAt.AddHours(-LeadHours));
    }

    [Fact]
    public async Task Si_el_paciente_la_reprograma_y_vuelve_a_pendiente_anula_el_automatico_sin_programar_otro()
    {
        var pendingAppointment = WithId(AppointmentMother.InStatus(AppointmentStatus.Pending));
        var automatic = Pending();
        GivenPending(automatic);

        await Rescheduled().HandleAsync(
            new AppointmentRescheduledEvent(pendingAppointment, AppointmentParticipant.Patient, pendingAppointment.StartsAt.AddDays(-1)),
            CancellationToken.None);

        automatic.Status.ShouldBe(ReminderStatus.Cancelled);
        _added.ShouldBeEmpty();
    }

    private ScheduleReminderOnAppointmentConfirmed Confirmed() => new(Scheduler());

    private RecalculateReminderOnAppointmentRescheduled Rescheduled() => new(Scheduler());

    private AutomaticReminderScheduler Scheduler() => new(
        _reminders,
        _templates,
        _appointmentReader,
        new ReminderComposer(new TemplateRenderer()),
        _unitOfWork,
        _clock,
        Options.Create(new SchedulingOptions { PatientReminderLeadHours = LeadHours }));

    private void GivenPending(params Reminder[] reminders) =>
        _reminders.ListPendingByAppointmentAsync(AppointmentId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Reminder>)reminders.ToList());

    private Reminder Pending(int? createdBy = null) =>
        Reminder.Schedule(AppointmentId, createdBy is null ? ReminderTemplateMother.GlobalId : null, "Texto",
            _confirmed.StartsAt.AddHours(-LeadHours), _clock.UtcNow, createdBy).Value;

    private static AppointmentNotificationData Data() => new(
        AppointmentId, "Ana Pérez", "ana@correo.com", "Luis Gómez", "luis@correo.com", "Cardiología", 2500m,
        new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0), null);

    private static Appointment WithId(Appointment appointment)
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, AppointmentId);
        return appointment;
    }
}
