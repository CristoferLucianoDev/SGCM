using SGCM.Data.Domain.Common;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common;

/// <summary>
/// Pruebas de la baja lógica de <see cref="ActivatableAggregateRoot"/>, usando
/// <c>User</c> como agregado concreto.
/// </summary>
public class ActivatableAggregateRootTests
{
    [Fact]
    public void Activate_en_un_registro_activo_falla_con_el_nombre_de_la_entidad()
    {
        var user = UserMother.Pending();

        var result = user.Activate();

        result.Error.ShouldBe(ActivationErrors.AlreadyActive("User"));
    }

    [Fact]
    public void Deactivate_y_Activate_cambian_el_estado()
    {
        var user = UserMother.Pending();

        user.Deactivate().IsSuccess.ShouldBeTrue();
        user.IsActive.ShouldBeFalse();

        user.Activate().IsSuccess.ShouldBeTrue();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void Deactivate_en_un_registro_inactivo_falla()
    {
        var user = UserMother.Pending();
        user.Deactivate();

        var result = user.Deactivate();

        result.Error.ShouldBe(ActivationErrors.AlreadyInactive("User"));
    }
}
