using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

using Unit = SGCM.Data.Domain.Common.Unit;

/// <summary>
/// Pruebas de los comandos de plantillas: el médico solo gestiona las suyas; una global se rechaza por ser de solo
/// lectura y una de otro médico responde como inexistente.
/// </summary>
public sealed class ReminderTemplateCommandHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private const int OtherDoctorId = 8;
    private const int OwnId = 5;
    private const int OthersId = 6;
    private const int SavedId = 900;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IReminderTemplateRepository _templates = Substitute.For<IReminderTemplateRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly List<ReminderTemplate> _added = [];
    private readonly ReminderTemplate _own = ReminderTemplateMother.Own(DoctorId, OwnId);
    private readonly DoctorReminderTemplateAccess _access;

    public ReminderTemplateCommandHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)DoctorId);
        _templates.GetByIdForUpdateAsync(OwnId, Arg.Any<CancellationToken>()).Returns(_own);
        _templates.GetByIdForUpdateAsync(OthersId, Arg.Any<CancellationToken>())
            .Returns(ReminderTemplateMother.Own(OtherDoctorId, OthersId));
        _templates.GetByIdForUpdateAsync(ReminderTemplateMother.GlobalId, Arg.Any<CancellationToken>())
            .Returns(ReminderTemplateMother.Global());
        _templates.When(templates => templates.Add(Arg.Any<ReminderTemplate>())).Do(call => _added.Add(call.Arg<ReminderTemplate>()));
        // Simula la base: al guardar, la plantilla nueva recibe su id.
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()))
            .Do(_ => _added.ForEach(template => typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(template, SavedId)));
        _access = new DoctorReminderTemplateAccess(_currentUser, _doctors, _templates);
    }

    [Fact]
    public async Task Crear_guarda_una_plantilla_activa_del_medico_y_la_devuelve_con_su_id()
    {
        var result = await Create("Indicaciones", "Su cita de {SpecialtyName}", "Hola {PatientName}.");

        var template = _added.ShouldHaveSingleItem();
        template.DoctorId.ShouldBe(DoctorId);
        result.Value.ShouldBe(new ReminderTemplateDto(
            SavedId, "Indicaciones", "Su cita de {SpecialtyName}", "Hola {PatientName}.", IsActive: true, IsReadOnly: false));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Crear_con_una_variable_no_permitida_se_rechaza_sin_guardar()
    {
        var result = await Create("Indicaciones", "Asunto", "Hola {Paciente}.");

        result.Error.Code.ShouldBe("ReminderTemplate.UnknownVariable");
        _added.ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editar_reemplaza_los_datos_de_la_plantilla_propia()
    {
        var result = await Update(OwnId, "Nuevo", "Asunto nuevo", "Hola {PatientName}, texto nuevo.");

        result.Value.ShouldBe(new ReminderTemplateDto(
            OwnId, "Nuevo", "Asunto nuevo", "Hola {PatientName}, texto nuevo.", IsActive: true, IsReadOnly: false));
        _own.Name.ShouldBe("Nuevo");
    }

    [Fact]
    public async Task Desactivar_deja_inactiva_la_plantilla_propia_y_una_segunda_vez_es_conflicto()
    {
        (await Deactivate(OwnId)).IsSuccess.ShouldBeTrue();
        _own.IsActive.ShouldBeFalse();

        var again = await Deactivate(OwnId);
        again.Error.Code.ShouldBe("ReminderTemplate.AlreadyInactive");
        again.Error.Type.ShouldBe(ErrorType.Conflict);
    }

    [Fact]
    public async Task Una_plantilla_global_no_se_edita_ni_se_desactiva()
    {
        (await Update(ReminderTemplateMother.GlobalId, "Otro", "Otro", "Otro")).Error.ShouldBe(ReminderTemplateErrors.GlobalReadOnly);
        (await Deactivate(ReminderTemplateMother.GlobalId)).Error.ShouldBe(ReminderTemplateErrors.GlobalReadOnly);
    }

    [Fact]
    public async Task Una_plantilla_de_otro_medico_o_inexistente_responde_como_inexistente()
    {
        (await Update(OthersId, "Otro", "Otro", "Otro")).Error.ShouldBe(ReminderTemplateErrors.NotFound);
        (await Deactivate(OthersId)).Error.ShouldBe(ReminderTemplateErrors.NotFound);
        (await Update(404, "Otro", "Otro", "Otro")).Error.ShouldBe(ReminderTemplateErrors.NotFound);
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_se_rechaza_en_los_tres_comandos()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        (await Create("Nombre", "Asunto", "Cuerpo")).Error.ShouldBe(ReminderTemplateErrors.DoctorOnly);
        (await Update(OwnId, "Nombre", "Asunto", "Cuerpo")).Error.ShouldBe(ReminderTemplateErrors.DoctorOnly);
        (await Deactivate(OwnId)).Error.ShouldBe(ReminderTemplateErrors.DoctorOnly);
        _templates.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_responde_medico_no_encontrado()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await Create("Nombre", "Asunto", "Cuerpo")).Error.ShouldBe(DoctorErrors.NotFound);
        (await Deactivate(OwnId)).Error.ShouldBe(DoctorErrors.NotFound);
    }

    private Task<Result<ReminderTemplateDto>> Create(string name, string subject, string body) =>
        new CreateReminderTemplateCommandHandler(_access, _templates, _unitOfWork)
            .HandleAsync(new CreateReminderTemplateCommand(name, subject, body), CancellationToken.None);

    private Task<Result<ReminderTemplateDto>> Update(int id, string name, string subject, string body) =>
        new UpdateReminderTemplateCommandHandler(_access)
            .HandleAsync(new UpdateReminderTemplateCommand(id, name, subject, body), CancellationToken.None);

    private Task<Result<Unit>> Deactivate(int id) =>
        new DeactivateReminderTemplateCommandHandler(_access)
            .HandleAsync(new DeactivateReminderTemplateCommand(id), CancellationToken.None);
}
