using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del paso pendiente del usuario en <see cref="User.GetPendingStep"/>.
/// </summary>
public class UserPendingStepTests
{
    [Fact]
    public void GetPendingStep_con_contrasena_temporal_devuelve_cambiarla()
    {
        var user = UserMother.WithRole(UserRole.Doctor, mustChangePassword: true);

        user.GetPendingStep(hasProfile: false).ShouldBe(PendingStep.ChangeTemporaryPassword);
    }

    [Fact]
    public void GetPendingStep_la_contrasena_temporal_va_antes_que_el_perfil()
    {
        var user = UserMother.WithRole(UserRole.Doctor, mustChangePassword: true);

        user.GetPendingStep(hasProfile: true).ShouldBe(PendingStep.ChangeTemporaryPassword);
    }

    [Fact]
    public void GetPendingStep_de_un_medico_sin_perfil_devuelve_su_perfil()
    {
        var user = UserMother.WithRole(UserRole.Doctor);

        user.GetPendingStep(hasProfile: false).ShouldBe(PendingStep.DoctorProfile);
    }

    [Fact]
    public void GetPendingStep_de_un_paciente_sin_perfil_devuelve_su_perfil()
    {
        var user = UserMother.WithRole(UserRole.Patient);

        user.GetPendingStep(hasProfile: false).ShouldBe(PendingStep.PatientProfile);
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    public void GetPendingStep_con_perfil_y_sin_contrasena_temporal_devuelve_null(UserRole role)
    {
        var user = UserMother.WithRole(role);

        user.GetPendingStep(hasProfile: true).ShouldBeNull();
    }

    // El PBI solo define pasos de perfil para médico y paciente.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GetPendingStep_de_un_administrador_no_pide_perfil(bool hasProfile)
    {
        var user = UserMother.WithRole(UserRole.Administrator);

        user.GetPendingStep(hasProfile).ShouldBeNull();
    }

    [Fact]
    public void GetPendingStep_de_un_administrador_con_contrasena_temporal_devuelve_cambiarla()
    {
        var user = UserMother.WithRole(UserRole.Administrator, mustChangePassword: true);

        user.GetPendingStep(hasProfile: true).ShouldBe(PendingStep.ChangeTemporaryPassword);
    }
}
