using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Messaging.Commands.ScheduleReminder;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

/// <summary>
/// Pruebas de <see cref="ScheduleReminderCommandHandler"/>: el médico programa recordatorios para sus citas confirmadas
/// o completadas a una hora futura; se guarda el texto final, la plantilla como referencia y quién lo programó.
/// </summary>
public sealed class ScheduleReminderCommandHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int SavedId = 700;
    private const int PrivateTemplateId = 5;
    private const int OtherDoctorId = AppointmentMother.DoctorId + 1;
    private const string Message = "Hola Ana Pérez, le recordamos su cita de Cardiología del 12/01/2027 a las 09:00.";

    private static readonly DateTimeOffset Now = new(2027, 1, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SendAt = new(2027, 1, 11, 9, 0, 0, AppointmentMother.SantoDomingo);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IReminderTemplateRepository _templates = Substitute.For<IReminderTemplateRepository>();
    private readonly IReminderRepository _reminders = Substitute.For<IReminderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = Now };
    private readonly List<Reminder> _added = [];
    private readonly ScheduleReminderCommandHandler _handler;

    private Appointment _appointment = AppointmentWithId(AppointmentStatus.Confirmed);

    public ScheduleReminderCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_ => _appointment);
        _templates.GetByIdAsync(ReminderTemplateMother.GlobalId, Arg.Any<CancellationToken>())
            .Returns(ReminderTemplateMother.Global());
        _reminders.When(reminders => reminders.Add(Arg.Any<Reminder>())).Do(call => _added.Add(call.Arg<Reminder>()));
        // Simula la base: al guardar, el recordatorio nuevo recibe su id.
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => _added.Where(reminder => reminder.Id == 0).ToList()
                .ForEach(reminder => reminder.GetType().GetProperty("Id")!.SetValue(reminder, SavedId)));

        _handler = new ScheduleReminderCommandHandler(
            new DoctorAppointmentAccess(_currentUser, _doctors, _appointments), _templates, _reminders, _currentUser,
            _unitOfWork, _clock);
    }

    [Fact]
    public async Task Programa_el_texto_editado_de_una_plantilla_con_su_referencia_y_quien_lo_programo()
    {
        var result = await Handle(ReminderTemplateMother.GlobalId, $"  {Message} Traiga sus estudios.  ");

        var reminder = _added.ShouldHaveSingleItem();
        reminder.AppointmentId.ShouldBe(AppointmentId);
        reminder.TemplateId.ShouldBe(ReminderTemplateMother.GlobalId);
        reminder.MessageBody.ShouldBe($"{Message} Traiga sus estudios.");
        reminder.ScheduledAt.ShouldBe(SendAt.ToUniversalTime());
        reminder.Status.ShouldBe(ReminderStatus.Pending);
        reminder.CreatedBy.ShouldBe(UserId);
        result.Value.ShouldBe(new ScheduledReminderDto(
            SavedId, AppointmentId, ReminderTemplateMother.GlobalId, $"{Message} Traiga sus estudios.", SendAt.ToUniversalTime()));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_mensaje_libre_se_programa_sin_plantilla()
    {
        var result = await Handle(templateId: null, "Recuerde ayunar 8 horas antes.");

        result.IsSuccess.ShouldBeTrue();
        var reminder = _added.ShouldHaveSingleItem();
        reminder.TemplateId.ShouldBeNull();
        reminder.MessageBody.ShouldBe("Recuerde ayunar 8 horas antes.");
        _templates.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Admite_una_cita_completada_y_una_plantilla_privada_del_medico()
    {
        _appointment = AppointmentWithId(AppointmentStatus.Completed);
        _templates.GetByIdAsync(PrivateTemplateId, Arg.Any<CancellationToken>()).Returns(
            ReminderTemplateMother.Create(PrivateTemplateId, AppointmentMother.DoctorId, "Seguimiento", "Asunto", "Texto"));

        (await Handle(PrivateTemplateId, "¿Cómo sigue después de su consulta?")).IsSuccess.ShouldBeTrue();
        _added.ShouldHaveSingleItem().TemplateId.ShouldBe(PrivateTemplateId);
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Una_cita_que_no_esta_confirmada_ni_completada_se_rechaza(AppointmentStatus status)
    {
        _appointment = AppointmentWithId(status);

        AssertRejected(await Handle(ReminderTemplateMother.GlobalId, Message), ReminderErrors.AppointmentNotEligible);
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_como_inexistente()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)OtherDoctorId);

        AssertRejected(await Handle(ReminderTemplateMother.GlobalId, Message), AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_fecha_pasada_o_la_hora_actual_se_rechazan()
    {
        AssertRejected(await Handle(null, Message, Now.AddMinutes(-1)), ReminderErrors.ScheduledInPast);
        AssertRejected(await Handle(null, Message, Now), ReminderErrors.ScheduledInPast);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task Un_mensaje_vacio_se_rechaza(string? message)
    {
        AssertRejected(await Handle(null, message), ReminderErrors.MessageRequired);
    }

    [Fact]
    public async Task Un_mensaje_de_mas_de_2000_caracteres_se_rechaza()
    {
        AssertRejected(await Handle(null, new string('A', Reminder.MessageMaxLength + 1)), ReminderErrors.MessageTooLong);
    }

    [Fact]
    public async Task Un_mensaje_de_2000_caracteres_se_acepta()
    {
        (await Handle(null, new string('A', Reminder.MessageMaxLength))).IsSuccess.ShouldBeTrue();
    }

    public static IEnumerable<object?[]> UnusableTemplates()
    {
        yield return [null];
        yield return [ReminderTemplateMother.Create(PrivateTemplateId, null, "Inactiva", "Asunto", "Texto", isActive: false)];
        yield return [ReminderTemplateMother.Create(PrivateTemplateId, OtherDoctorId, "Ajena", "Asunto", "Texto")];
    }

    [Theory]
    [MemberData(nameof(UnusableTemplates))]
    public async Task Una_plantilla_inexistente_inactiva_o_de_otro_medico_se_rechaza(ReminderTemplate? template)
    {
        _templates.GetByIdAsync(PrivateTemplateId, Arg.Any<CancellationToken>()).Returns(template);

        AssertRejected(await Handle(PrivateTemplateId, Message), ReminderTemplateErrors.NotFound);
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_se_rechaza()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        AssertRejected(await Handle(null, Message), ReminderErrors.DoctorOnly);
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_responde_medico_no_encontrado()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        AssertRejected(await Handle(null, Message), DoctorErrors.NotFound);
    }

    private Task<Result<ScheduledReminderDto>> Handle(int? templateId, string? message, DateTimeOffset? scheduledAt = null) =>
        _handler.HandleAsync(
            new ScheduleReminderCommand(AppointmentId, templateId, message, scheduledAt ?? SendAt), CancellationToken.None);

    private void AssertRejected(Result<ScheduledReminderDto> result, Error error)
    {
        result.Error.ShouldBe(error);
        _reminders.DidNotReceive().Add(Arg.Any<Reminder>());
        _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static Appointment AppointmentWithId(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        // Entity.Id solo lo asigna la base; aquí se imita lo que hace EF al cargar.
        appointment.GetType().GetProperty("Id")!.SetValue(appointment, AppointmentId);
        return appointment;
    }
}
