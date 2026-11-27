using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Application.Clinical;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common;
using SGCM.Test.Common.Fakes;

namespace SGCM.Test.Unit.Application.Behaviors;
using Unit = SGCM.Data.Domain.Common.Unit;

/// <summary>
/// Pruebas de <see cref="AccessLogDecorator{TCommand,TResponse}"/>: cada acceso que el handler informa queda en el
/// registro de accesos, con el id que la versión recibe al guardarse, el usuario autenticado, la hora y la IP.
/// </summary>
public sealed class AccessLogDecoratorTests
{
    private const int UserId = 30;
    private const int FirstSavedId = 41;
    private const string IpAddress = "10.0.0.5";

    private static readonly Error HandlerError = Error.Conflict("Test.Conflict", "Conflicto de prueba.");
    private static readonly DateTimeOffset RecordedAt = new(2026, 11, 2, 18, 30, 0, TimeSpan.Zero);

    private readonly ICommandHandler<TestCommand, Unit> _inner = Substitute.For<ICommandHandler<TestCommand, Unit>>();
    private readonly MedicalRecordAccessTracker _tracker = new();
    private readonly IMedicalRecordAccessLogRepository _repository = Substitute.For<IMedicalRecordAccessLogRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = RecordedAt.AddSeconds(1) };
    private readonly List<MedicalRecord> _unsaved = [];
    private readonly AccessLogDecorator<TestCommand, Unit> _decorator;

    public AccessLogDecoratorTests()
    {
        _currentUser.UserId.Returns(UserId);
        // Simula la base: al guardar, cada versión nueva recibe su id.
        _unitOfWork.When(unitOfWork => unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ =>
        {
            var nextId = FirstSavedId;
            foreach (var record in _unsaved.Where(record => record.Id == 0)) AssignId(record, nextId++);
        });
        _decorator = new AccessLogDecorator<TestCommand, Unit>(
            _inner, _tracker, _repository, _unitOfWork, _currentUser, _clock);
    }

    [Fact]
    public async Task Registra_la_creacion_con_el_id_guardado_el_usuario_la_hora_y_la_ip()
    {
        HandlerTracks(MedicalRecordAccessType.Create);

        var result = await _decorator.HandleAsync(new TestCommand(IpAddress), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var log = AddedLogs().ShouldHaveSingleItem();
        log.MedicalRecordId.ShouldBe(FirstSavedId);
        log.AccessedBy.ShouldBe(UserId);
        log.AccessType.ShouldBe(MedicalRecordAccessType.Create);
        log.AccessedAt.ShouldBe(_clock.UtcNow);
        log.IpAddress.ShouldBe(IpAddress);
    }

    [Fact]
    public async Task Guarda_antes_de_leer_el_id_y_agrega_el_registro_despues()
    {
        HandlerTracks(MedicalRecordAccessType.Update);

        await _decorator.HandleAsync(new TestCommand(IpAddress), CancellationToken.None);

        Received.InOrder(() =>
        {
            _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
            _repository.Add(Arg.Any<MedicalRecordAccessLog>());
        });
    }

    [Fact]
    public async Task Registra_cada_acceso_informado_en_su_orden()
    {
        HandlerTracks(MedicalRecordAccessType.View, MedicalRecordAccessType.Update);

        await _decorator.HandleAsync(new TestCommand(IpAddress), CancellationToken.None);

        AddedLogs().Select(log => (log.MedicalRecordId, log.AccessType)).ShouldBe(
        [
            (FirstSavedId, MedicalRecordAccessType.View),
            (FirstSavedId + 1, MedicalRecordAccessType.Update)
        ]);
    }

    [Fact]
    public async Task Si_el_handler_falla_no_guarda_ni_registra_nada()
    {
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(HandlerError);

        var result = await _decorator.HandleAsync(new TestCommand(IpAddress), CancellationToken.None);

        result.Error.ShouldBe(HandlerError);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<MedicalRecordAccessLog>());
    }

    [Fact]
    public async Task Un_comando_que_termina_bien_sin_informar_accesos_es_un_error_de_programacion()
    {
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(Unit.Value);

        await Should.ThrowAsync<InvalidOperationException>(() =>
            _decorator.HandleAsync(new TestCommand(IpAddress), CancellationToken.None));
        _repository.DidNotReceive().Add(Arg.Any<MedicalRecordAccessLog>());
    }

    [Fact]
    public async Task Los_accesos_registrados_no_se_registran_otra_vez()
    {
        HandlerTracks(MedicalRecordAccessType.Create);
        await _decorator.HandleAsync(new TestCommand(IpAddress), CancellationToken.None);

        _tracker.Accesses.ShouldBeEmpty();
    }

    private void HandlerTracks(params MedicalRecordAccessType[] accessTypes)
    {
        _inner.HandleAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            foreach (var accessType in accessTypes)
            {
                var record = NewRecord();
                _unsaved.Add(record);
                _tracker.Track(record, accessType);
            }

            return Result<Unit>.Success(Unit.Value);
        });
    }

    private List<MedicalRecordAccessLog> AddedLogs() =>
        _repository.ReceivedCalls().Select(call => (MedicalRecordAccessLog)call.GetArguments()[0]!).ToList();

    private static MedicalRecord NewRecord() => MedicalRecord.Create(
        appointmentId: 10, patientId: 20, MedicalRecordContent.Create("Faringitis", null, null).Value, UserId, RecordedAt);

    // Entity.Id solo lo asigna la base; aquí se imita lo que hace EF al guardar.
    private static void AssignId(MedicalRecord record, int id) =>
        typeof(MedicalRecord).GetProperty(nameof(MedicalRecord.Id))!.SetValue(record, id);

    public sealed record TestCommand(string? IpAddress) : IAccessLoggedCommand<Unit>;
}
