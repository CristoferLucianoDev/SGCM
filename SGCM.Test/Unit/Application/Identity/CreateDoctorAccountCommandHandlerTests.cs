using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Unit.Application.Identity;

public sealed class CreateDoctorAccountCommandHandlerTests
{
    private const string DoctorEmail = "medico@correo.com";
    private const string TemporaryPassword = "Kq7#mPz2vXr9";
    private const string TemporaryPasswordHash = "hash-temporal";

    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IPasswordHasher _passwordHasher = Substitute.For<IPasswordHasher>();
    private readonly ITemporaryPasswordGenerator _passwordGenerator = Substitute.For<ITemporaryPasswordGenerator>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly CreateDoctorAccountCommandHandler _handler;

    public CreateDoctorAccountCommandHandlerTests()
    {
        _passwordGenerator.Generate().Returns(TemporaryPassword);
        _passwordHasher.Hash(Arg.Any<Password>()).Returns(TemporaryPasswordHash);

        _handler = new CreateDoctorAccountCommandHandler(
            _userRepository, _passwordHasher, _passwordGenerator, _unitOfWork);
    }

    [Fact]
    public async Task Un_correo_nuevo_crea_el_medico_con_la_contrasena_temporal()
    {
        var result = await _handler.HandleAsync(new CreateDoctorAccountCommand(DoctorEmail), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Email.ShouldBe(DoctorEmail);
        result.Value.TemporaryPassword.ShouldBe(TemporaryPassword);

        _passwordHasher.Received(1).Hash(Arg.Is<Password>(password => password.Value == TemporaryPassword));
        _userRepository.Received(1).Add(Arg.Is<User>(user =>
            user.Role == UserRole.Doctor
            && user.PasswordHash == TemporaryPasswordHash
            && user.MustChangePassword
            && user.IsEmailConfirmed));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_correo_existente_responde_EmailAlreadyInUse_sin_crear_nada()
    {
        _userRepository
            .GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>())
            .Returns(UserMother.Confirmed());

        var result = await _handler.HandleAsync(new CreateDoctorAccountCommand(DoctorEmail), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.EmailAlreadyInUse);
        _passwordGenerator.DidNotReceive().Generate();
        _userRepository.DidNotReceive().Add(Arg.Any<User>());
    }

    [Fact]
    public async Task Un_correo_invalido_responde_error_sin_consultar_el_repositorio()
    {
        var result = await _handler.HandleAsync(new CreateDoctorAccountCommand("no-es-un-correo"), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void El_resultado_oculta_la_contrasena_en_ToString()
    {
        var result = new CreateDoctorAccountResult(1, DoctorEmail, TemporaryPassword);

        result.ToString().ShouldNotContain(TemporaryPassword);
    }
}