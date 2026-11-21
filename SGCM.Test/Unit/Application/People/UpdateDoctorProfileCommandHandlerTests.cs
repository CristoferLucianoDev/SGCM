using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.UpdateDoctorProfile;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Unit.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class UpdateDoctorProfileCommandHandlerTests
{
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IDoctorRepository _doctors = Substitute.For<IDoctorRepository>();
    private readonly IDoctorProfileCatalogReader _catalog = Substitute.For<IDoctorProfileCatalogReader>();
    private readonly PasswordSessionScenario _identity = new(mustChangePassword: false);
    private readonly UpdateDoctorProfileCommand _command = UpdateDoctorProfileCommandValidatorTests.ValidCommand();
    private readonly Doctor _doctor;
    private readonly UpdateDoctorProfileCommandHandler _handler;

    public UpdateDoctorProfileCommandHandlerTests()
    {
        _doctor = DoctorMother.Create([11, 22], userId: _identity.Owner.Id).Value;
        SetSession(_identity.Current.Id);
        SetUser(_identity.Owner);
        _doctors.GetByUserIdForUpdateAsync(_identity.Owner.Id, Arg.Any<CancellationToken>()).Returns(_doctor);
        SetLocation(new(true, _command.ProvinceId));
        _handler = new(_currentUser, _users, _doctors, _catalog, new FakeDateTimeProvider());
    }

    [Fact]
    public async Task Actualiza_el_perfil_de_la_identidad_autenticada_conservando_identidad_y_vinculos()
    {
        using var cancellation = new CancellationTokenSource();
        var token = cancellation.Token;

        (await _handler.HandleAsync(_command, token)).IsSuccess.ShouldBeTrue();

        Received.InOrder(() =>
        {
            _users.GetByIdForUpdateAsync(_identity.Owner.Id, token);
            _doctors.GetByUserIdForUpdateAsync(_identity.Owner.Id, token);
            _catalog.GetLocationAsync(_command.ProvinceId, _command.MunicipalityId, token);
        });
        _doctor.PhoneNumber.Value.ShouldBe("809-555-0123");
        _doctor.UserId.ShouldBe(_identity.Owner.Id);
        _doctor.FirstName.ShouldBe(DoctorMother.FirstName);
        _doctor.LastName.ShouldBe(DoctorMother.LastName);
        _doctor.LicenseNumber.ShouldBe(DoctorMother.LicenseNumber);
        _doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(new[] { 11, 22 });
    }

    [Theory]
    [InlineData("sin identidad", "User.SessionInvalid")]
    [InlineData("ausente", "User.SessionInvalid")]
    [InlineData("ajena", "User.SessionInvalid")]
    [InlineData("revocada", "User.SessionInvalid")]
    [InlineData("expirada", "User.SessionInvalid")]
    [InlineData("inactivo", "User.AccountInactive")]
    [InlineData("sin confirmar", "User.EmailNotConfirmed")]
    [InlineData("paciente", "Doctor.RoleRequired")]
    [InlineData("administrador", "Doctor.RoleRequired")]
    [InlineData("temporal", "Doctor.TemporaryPasswordChangeRequired")]
    public async Task Rechaza_cuenta_o_sesion_invalidas_antes_de_cargar_el_perfil(string scenario, string code)
    {
        switch (scenario)
        {
            case "sin identidad":
                _currentUser.TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(false);
                break;
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

        _doctors.ReceivedCalls().ShouldBeEmpty();
        _catalog.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task El_perfil_propio_inexistente_devuelve_not_found()
    {
        _doctors.GetByUserIdForUpdateAsync(_identity.Owner.Id, Arg.Any<CancellationToken>()).Returns((Doctor?)null);

        (await _handler.HandleAsync(_command, CancellationToken.None)).Error.ShouldBe(DoctorErrors.NotFound);

        _catalog.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, null, "Doctor.ProvinceNotFound")]
    [InlineData(true, null, "Doctor.MunicipalityNotFound")]
    [InlineData(true, 5, "Doctor.MunicipalityProvinceMismatch")]
    public async Task Rechaza_geografia_invalida_antes_de_mutar(bool provinceExists, int? municipalityProvince, string code)
    {
        SetLocation(new(provinceExists, municipalityProvince));

        (await _handler.HandleAsync(_command, CancellationToken.None)).Error.Code.ShouldBe(code);

        _doctor.PhoneNumber.Value.ShouldBe(DoctorMother.PhoneNumber);
        _doctor.AddressLine.ShouldBe(DoctorMother.AddressLine);
        _doctor.ProvinceId.ShouldBe(DoctorMother.ProvinceId);
        _doctor.MunicipalityId.ShouldBe(DoctorMother.MunicipalityId);
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
        _catalog.GetLocationAsync(_command.ProvinceId, _command.MunicipalityId, Arg.Any<CancellationToken>()).Returns(location);
}
