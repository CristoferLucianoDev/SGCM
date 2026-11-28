using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Messaging;
using SGCM.Data.Application.Messaging.Queries.GetReminderPreview;
using SGCM.Data.Application.Scheduling.Notifications;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.Reminders;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Infrastructure.Notifications;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

/// <summary>
/// Pruebas de <see cref="GetReminderPreviewQueryHandler"/>: la plantilla elegida sale completada con los datos reales
/// de la cita, y solo para una cita propia confirmada o completada con una plantilla activa, global o del médico.
/// </summary>
public sealed class GetReminderPreviewQueryHandlerTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int PrivateTemplateId = 5;
    private const int OtherDoctorId = AppointmentMother.DoctorId + 1;

    private static readonly AppointmentNotificationData Data = new(
        AppointmentId, "Ana Pérez", "ana@correo.com", "Luis Gómez", "luis@correo.com", "Cardiología", 2500m,
        new DateOnly(2027, 1, 12), new TimeOnly(9, 0), new TimeOnly(11, 0), null);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IReminderTemplateRepository _templates = Substitute.For<IReminderTemplateRepository>();
    private readonly IAppointmentNotificationReader _appointmentData = Substitute.For<IAppointmentNotificationReader>();

    private Appointment _appointment = AppointmentWithId(AppointmentStatus.Confirmed);

    public GetReminderPreviewQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_ => _appointment);
        _templates.GetByIdAsync(ReminderTemplateMother.GlobalId, Arg.Any<CancellationToken>())
            .Returns(ReminderTemplateMother.Global());
        _appointmentData.GetAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(Data);
    }

    [Fact]
    public async Task Devuelve_el_asunto_y_el_texto_de_la_plantilla_con_los_datos_reales_de_la_cita()
    {
        var result = await Handle(AppointmentId, ReminderTemplateMother.GlobalId);

        result.Value.ShouldBe(new ReminderPreviewDto(
            ReminderTemplateMother.GlobalId,
            "Recordatorio: su cita de Cardiología es manana",
            "Hola Ana Pérez, le recordamos su cita de Cardiología con Luis Gómez el 12/01/2027 a las 09:00."));
    }

    [Fact]
    public async Task Admite_una_cita_completada_y_una_plantilla_privada_del_medico()
    {
        _appointment = AppointmentWithId(AppointmentStatus.Completed);
        _templates.GetByIdAsync(PrivateTemplateId, Arg.Any<CancellationToken>()).Returns(ReminderTemplateMother.Create(
            PrivateTemplateId, AppointmentMother.DoctorId, "Seguimiento", "Seguimiento de {SpecialtyName}",
            "Hola {PatientName}, ¿cómo sigue después de su cita del {AppointmentDate}?"));

        var result = await Handle(AppointmentId, PrivateTemplateId);

        result.Value.ShouldBe(new ReminderPreviewDto(
            PrivateTemplateId, "Seguimiento de Cardiología", "Hola Ana Pérez, ¿cómo sigue después de su cita del 12/01/2027?"));
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending)]
    [InlineData(AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Una_cita_que_no_esta_confirmada_ni_completada_se_rechaza(AppointmentStatus status)
    {
        _appointment = AppointmentWithId(status);

        (await Handle(AppointmentId, ReminderTemplateMother.GlobalId)).Error.ShouldBe(ReminderErrors.AppointmentNotEligible);
        _appointmentData.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_como_inexistente()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)OtherDoctorId);

        (await Handle(AppointmentId, ReminderTemplateMother.GlobalId)).Error.ShouldBe(AppointmentErrors.NotFound);
        _templates.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_inexistente_responde_cita_no_encontrada()
    {
        _appointments.GetByIdAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        (await Handle(AppointmentId, ReminderTemplateMother.GlobalId)).Error.ShouldBe(AppointmentErrors.NotFound);
    }

    public static IEnumerable<object?[]> UnusableTemplates()
    {
        yield return [null];
        yield return [ReminderTemplateMother.Global(isActive: false)];
        yield return [ReminderTemplateMother.Create(PrivateTemplateId, OtherDoctorId, "Ajena", "Asunto", "Texto")];
    }

    [Theory]
    [MemberData(nameof(UnusableTemplates))]
    public async Task Una_plantilla_inexistente_inactiva_o_de_otro_medico_responde_plantilla_no_encontrada(
        ReminderTemplate? template)
    {
        _templates.GetByIdAsync(PrivateTemplateId, Arg.Any<CancellationToken>()).Returns(template);

        var result = await Handle(AppointmentId, PrivateTemplateId);

        result.Error.ShouldBe(ReminderTemplateErrors.NotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        _appointmentData.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, 1, "Appointment.InvalidAppointmentId")]
    [InlineData(99, 0, "ReminderTemplate.InvalidId")]
    public async Task Identificadores_invalidos_se_rechazan_sin_leer(int appointmentId, int templateId, string code)
    {
        var result = await Handle(appointmentId, templateId);

        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_se_rechaza()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        (await Handle(AppointmentId, ReminderTemplateMother.GlobalId)).Error.ShouldBe(ReminderErrors.DoctorOnly);
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_responde_medico_no_encontrado()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await Handle(AppointmentId, ReminderTemplateMother.GlobalId)).Error.ShouldBe(DoctorErrors.NotFound);
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    private Task<Result<ReminderPreviewDto>> Handle(int appointmentId, int templateId) =>
        new GetReminderPreviewQueryHandler(
                _currentUser, _doctors, _appointments, _templates, _appointmentData,
                new ReminderComposer(new TemplateRenderer()), new GetReminderPreviewQueryValidator())
            .HandleAsync(new GetReminderPreviewQuery(appointmentId, templateId), CancellationToken.None);

    private static Appointment AppointmentWithId(AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        // Entity.Id solo lo asigna la base; aquí se imita lo que hace EF al cargar.
        appointment.GetType().GetProperty("Id")!.SetValue(appointment, AppointmentId);
        return appointment;
    }
}
