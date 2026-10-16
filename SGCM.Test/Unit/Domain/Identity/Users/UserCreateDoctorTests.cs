using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas de <see cref="User.CreateDoctor"/>.
/// </summary>
public class UserCreateDoctorTests
{
    private static readonly Email DoctorEmail = Email.Create("medico@correo.com").Value;

    [Fact]
    public void CreateDoctor_crea_un_medico_confirmado_activo_y_con_contrasena_temporal()
    {
        var user = User.CreateDoctor(DoctorEmail, "hash-temporal");

        user.Role.ShouldBe(UserRole.Doctor);
        user.IsEmailConfirmed.ShouldBeTrue();
        user.MustChangePassword.ShouldBeTrue();
        user.IsActive.ShouldBeTrue();
        user.PasswordHash.ShouldBe("hash-temporal");
        user.Email.ShouldBe(DoctorEmail);
    }

    [Fact]
    public void CreateDoctor_deja_como_paso_pendiente_cambiar_la_contrasena()
    {
        var user = User.CreateDoctor(DoctorEmail, "hash-temporal");

        user.GetPendingStep(hasProfile: false).ShouldBe(PendingStep.ChangeTemporaryPassword);
    }

    [Fact]
    public void CreateDoctor_no_emite_tokens_ni_eventos()
    {
        var user = User.CreateDoctor(DoctorEmail, "hash-temporal");

        user.Tokens.ShouldBeEmpty();
        user.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateDoctor_sin_hash_lanza_DomainException(string passwordHash)
    {
        Should.Throw<DomainException>(() => User.CreateDoctor(DoctorEmail, passwordHash));
    }
}