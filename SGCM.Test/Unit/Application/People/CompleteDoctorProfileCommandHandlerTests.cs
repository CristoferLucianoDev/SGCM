using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.CompleteDoctorProfile;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Unit.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class CompleteDoctorProfileCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IUserProfileReader _profiles = Substitute.For<IUserProfileReader>();
    private readonly IDoctorProfileCatalogReader _catalog = Substitute.For<IDoctorProfileCatalogReader>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly ISessionService _sessions = Substitute.For<ISessionService>();
    private readonly PasswordSessionScenario _identity = new(mustChangePassword: false);
    private readonly FakeDateTimeProvider _clock = new();
    private readonly CompleteDoctorProfileCommandHandler _handler;
    private readonly CompleteDoctorProfileCommand _command = CompleteDoctorProfileCommandValidatorTests.ValidCommand();
    private readonly SessionTokens _tokens = new(new("jwt", UserMother.Now.AddMinutes(15)),
        "refresh", UserMother.Now.AddDays(7), null);

    public CompleteDoctorProfileCommandHandlerTests()
    {
        SetSession(_identity.Current.Id);
        _users.GetByIdForUpdateAsync(_identity.Owner.Id, Arg.Any<CancellationToken>()).Returns(_identity.Owner);
        _catalog.GetLocationAsync(4, 9, Arg.Any<CancellationToken>()).Returns(new DoctorProfileLocation(true, 4));
        _catalog.GetSpecialtiesAsync(Arg.Any<int[]>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<DoctorProfileSpecialty>>([new(7, true), new(8, true)]);
        _sessions.StartAsync(_identity.Owner, _command.DeviceName, _command.IpAddress, Arg.Any<CancellationToken>())
            .Returns(_tokens);
        _handler = new(_currentUser, _users, _profiles, _catalog, _doctors, _sessions, _clock);
    }

    [Fact]
    public async Task Crea_para_la_identidad_autenticada_y_abre_sesion_despues_de_agregar_el_perfil()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        var result = await _handler.HandleAsync(_command, token);

        result.Value.ShouldBeSameAs(_tokens);
        Received.InOrder(() =>
        {
            _users.GetByIdForUpdateAsync(_identity.Owner.Id, token);
            _profiles.HasProfileAsync(_identity.Owner.Id, UserRole.Doctor, token);
            _catalog.GetLocationAsync(4, 9, token);
            _catalog.GetSpecialtiesAsync(Arg.Is<int[]>(ids => ids.SequenceEqual(new[] { 7, 8 })), token);
            _doctors.ExistsByLicenseNumberAsync("LIC-252", token);
            _doctors.Add(Arg.Is<Doctor>(d => d.UserId == _identity.Owner.Id && d.LicenseNumber == "LIC-252"
                && d.ProvinceId == 4 && d.MunicipalityId == 9 && d.PhoneNumber.Value == "809-555-0100"
                && d.ConsultationFee.Value == 12.34m && d.IsActive && d.Specialties.Count == 2));
            _sessions.StartAsync(_identity.Owner, "Navegador", "192.0.2.5", token);
        });
    }

    [Fact]
    public async Task Identidad_invalida_no_consulta_el_usuario()
    {
        _currentUser.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(false);

        (await _handler.HandleAsync(_command, CancellationToken.None)).Error.ShouldBe(UserErrors.SessionInvalid);

        _users.ReceivedCalls().ShouldBeEmpty();
        AssertNoWritesOrSession();
    }

    [Theory]
    [InlineData("ausente", "User.SessionInvalid")]
    [InlineData("ajena", "User.SessionInvalid")]
    [InlineData("revocada", "User.SessionInvalid")]
    [InlineData("expirada", "User.SessionInvalid")]
    [InlineData("inactivo", "User.AccountInactive")]
    [InlineData("sin confirmar", "User.EmailNotConfirmed")]
    [InlineData("paciente", "Doctor.RoleRequired")]
    [InlineData("administrador", "Doctor.RoleRequired")]
    [InlineData("temporal", "Doctor.TemporaryPasswordChangeRequired")]
    public async Task Rechaza_el_estado_actual_del_usuario_o_la_sesion_antes_de_consultar_el_perfil(string scenario, string code)
    {
        switch (scenario)
        {
            case "ausente": SetUser(null); break;
            case "ajena": SetSession(_identity.ForeignCurrent.Id); break;
            case "revocada": SetSession(_identity.Revoked.Id); break;
            case "expirada": SetSession(_identity.ExpiresNow.Id); break;
            case "inactivo": _identity.Owner.Deactivate().IsSuccess.ShouldBeTrue(); break;
            case "sin confirmar": SetUser(UserMother.Pending()); break;
            case "paciente": SetUser(UserMother.WithRole(UserRole.Patient)); break;
            case "administrador": SetUser(UserMother.WithRole(UserRole.Administrator)); break;
            case "temporal":
                var pending = new PasswordSessionScenario();
                SetUser(pending.Owner);
                SetSession(pending.Current.Id);
                break;
        }

        (await _handler.HandleAsync(_command, CancellationToken.None)).Error.Code.ShouldBe(code);

        _profiles.ReceivedCalls().ShouldBeEmpty();
        _catalog.ReceivedCalls().ShouldBeEmpty();
        AssertNoWritesOrSession();
    }

    [Theory]
    [InlineData("perfil", "Doctor.ProfileAlreadyExists")]
    [InlineData("dominio", "Doctor.InvalidConsultationFee")]
    [InlineData("provincia", "Doctor.ProvinceNotFound")]
    [InlineData("municipio", "Doctor.MunicipalityNotFound")]
    [InlineData("incompatible", "Doctor.MunicipalityProvinceMismatch")]
    [InlineData("especialidad", "Specialty.NotFound")]
    [InlineData("inactiva", "Specialty.Inactive")]
    [InlineData("licencia", "Doctor.LicenseNumberAlreadyInUse")]
    public async Task Un_perfil_rechazado_no_se_agrega_ni_abre_sesion(string scenario, string code)
    {
        var command = _command;
        switch (scenario)
        {
            case "perfil":
                _profiles.HasProfileAsync(_identity.Owner.Id, UserRole.Doctor, Arg.Any<CancellationToken>()).Returns(true);
                break;
            case "dominio": command = command with { ConsultationFee = 0.001m }; break;
            case "provincia": SetLocation(new(false, 4)); break;
            case "municipio": SetLocation(new(true, null)); break;
            case "incompatible": SetLocation(new(true, 5)); break;
            case "especialidad": SetSpecialties([new(7, true), new(9, true)]); break;
            case "inactiva": SetSpecialties([new(7, true), new(8, false)]); break;
            case "licencia":
                _doctors.ExistsByLicenseNumberAsync("LIC-252", Arg.Any<CancellationToken>()).Returns(true);
                break;
        }

        (await _handler.HandleAsync(command, CancellationToken.None)).Error.Code.ShouldBe(code);

        AssertNoWritesOrSession();
    }

    [Fact]
    public async Task Propaga_el_fallo_del_servicio_de_sesion_al_limite_transaccional()
    {
        _sessions.StartAsync(_identity.Owner, _command.DeviceName, _command.IpAddress, CancellationToken.None)
            .Returns(Result<SessionTokens>.Failure(UserErrors.SessionInvalid));

        var result = await _handler.HandleAsync(_command, CancellationToken.None);

        result.Error.ShouldBe(UserErrors.SessionInvalid);
        _doctors.Received(1).Add(Arg.Any<Doctor>());
    }

    private void SetSession(int sessionId) =>
        _currentUser.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(call =>
        {
            call[0] = _identity.Owner.Id;
            call[1] = sessionId;
            return true;
        });

    private void SetUser(User? user) =>
        _users.GetByIdForUpdateAsync(_identity.Owner.Id, Arg.Any<CancellationToken>()).Returns(user);

    private void SetLocation(DoctorProfileLocation location) =>
        _catalog.GetLocationAsync(4, 9, Arg.Any<CancellationToken>()).Returns(location);

    private void SetSpecialties(IReadOnlyList<DoctorProfileSpecialty> specialties) =>
        _catalog.GetSpecialtiesAsync(Arg.Any<int[]>(), Arg.Any<CancellationToken>()).Returns(specialties);

    private void AssertNoWritesOrSession()
    {
        _doctors.DidNotReceive().Add(Arg.Any<Doctor>());
        _sessions.ReceivedCalls().ShouldBeEmpty();
    }
}
