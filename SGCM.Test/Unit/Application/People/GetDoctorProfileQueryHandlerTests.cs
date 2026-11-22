using NSubstitute;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class GetDoctorProfileQueryHandlerTests
{
    private const int DoctorId = 42;
    private const int ViewerId = 8;
    private const int SessionId = 21;
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 15, 0, 0, TimeSpan.Zero);
    private static readonly DoctorPublicProfileDto PublicProfile = new(DoctorId, "Ana Núñez",
        [new(1, "Cardiología")], new(1, "Provincia"), new(2, "Municipio"), "Calle principal", 23.45m, 5, "Biografía");

    private readonly IDoctorProfileReader _reader = Substitute.For<IDoctorProfileReader>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IDateTimeProvider _clock = Substitute.For<IDateTimeProvider>();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Rechaza_id_invalido_antes_de_consultar(int id)
    {
        var result = await CreateHandler().HandleAsync(new GetDoctorProfileQuery(id), CancellationToken.None);

        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe("Doctor.InvalidDoctorId");
        _reader.ReceivedCalls().ShouldBeEmpty();
        _currentUser.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public async Task Solicitante_sin_privilegios_solo_consulta_el_perfil_publico(UserRole? role)
    {
        _currentUser.IsAuthenticated.Returns(role.HasValue);
        if (role.HasValue) _currentUser.IsInRole(role.Value).Returns(true);
        else _currentUser.IsInRole(UserRole.Administrator).Returns(true);
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        _reader.GetPublicAsync(DoctorId, ct).Returns(PublicProfile);

        var result = await CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), ct);

        result.Value.ShouldBeSameAs(PublicProfile);
        _reader.ReceivedCalls().ShouldHaveSingleItem();
        _currentUser.DidNotReceive().TryGetSessionIdentity(UserRole.Administrator, out Arg.Any<int>(), out Arg.Any<int>());
    }

    [Theory]
    [InlineData(false, ViewerId, SessionId)]
    [InlineData(true, 0, SessionId)]
    [InlineData(true, ViewerId, -1)]
    public async Task Un_claim_administrativo_sin_identidad_valida_solo_consulta_el_perfil_publico(
        bool valid, int userId, int sessionId)
    {
        SetAdministrator(userId, sessionId, valid);
        _reader.GetPublicAsync(DoctorId, CancellationToken.None).Returns(PublicProfile);

        var result = await CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), CancellationToken.None);

        result.Value.ShouldBeSameAs(PublicProfile);
        _reader.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Propaga_identidad_doctor_hora_y_cancelacion_a_la_lectura_administrativa()
    {
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        var adminProfile = new DoctorAdminProfileDto(PublicProfile, "809-555-0123", "LIC-258");
        SetAdministrator(ViewerId, SessionId, true);
        _reader.GetAdministrativeAsync(DoctorId, ViewerId, SessionId, Now, ct).Returns(adminProfile);

        var result = await CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), ct);

        result.Value.ShouldBeSameAs(adminProfile);
        _reader.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Si_la_lectura_administrativa_no_devuelve_una_fila_recurre_a_la_lectura_publica()
    {
        SetAdministrator(ViewerId, SessionId, true);
        _reader.GetPublicAsync(DoctorId, CancellationToken.None).Returns(PublicProfile);

        var result = await CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), CancellationToken.None);

        result.Value.ShouldBeSameAs(PublicProfile);
        Received.InOrder(() =>
        {
            _reader.GetAdministrativeAsync(DoctorId, ViewerId, SessionId, Now, CancellationToken.None);
            _reader.GetPublicAsync(DoctorId, CancellationToken.None);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Un_medico_sin_perfil_visible_devuelve_el_mismo_NotFound(bool administrator)
    {
        if (administrator) SetAdministrator(ViewerId, SessionId, true);

        var result = await CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), CancellationToken.None);

        result.Error.ShouldBe(DoctorErrors.PublicProfileNotFound);
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        await _reader.Received(1).GetPublicAsync(DoctorId, CancellationToken.None);
    }

    [Fact]
    public async Task Una_cancelacion_previa_no_consulta_al_lector()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), cancellation.Token));

        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Propaga_un_fallo_del_lector_administrativo_sin_hacer_una_lectura_publica()
    {
        SetAdministrator(ViewerId, SessionId, true);
        var failure = new PostgresException("Error de lectura simulado.", "ERROR", "ERROR", PostgresErrorCodes.InternalError);
        _reader.GetAdministrativeAsync(DoctorId, ViewerId, SessionId, Now, CancellationToken.None)
            .Returns(Task.FromException<DoctorAdminProfileDto?>(failure));

        var caught = await Should.ThrowAsync<PostgresException>(() =>
            CreateHandler().HandleAsync(new GetDoctorProfileQuery(DoctorId), CancellationToken.None));

        caught.ShouldBeSameAs(failure);
        _reader.ReceivedCalls().ShouldHaveSingleItem();
    }

    private void SetAdministrator(int userId, int sessionId, bool valid)
    {
        _clock.UtcNow.Returns(Now);
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Administrator).Returns(true);
        _currentUser.TryGetSessionIdentity(UserRole.Administrator, out Arg.Any<int>(), out Arg.Any<int>()).Returns(call =>
        {
            call[1] = userId;
            call[2] = sessionId;
            return valid;
        });
    }

    private GetDoctorProfileQueryHandler CreateHandler() => new(
        _reader, _currentUser, _clock, new GetDoctorProfileQueryValidator());
}
