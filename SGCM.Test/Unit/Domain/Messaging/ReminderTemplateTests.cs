using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Messaging;

public sealed class ReminderTemplateTests
{
    private const int DoctorId = 7;
    private const int OtherDoctorId = 8;

    [Fact]
    public void Una_plantilla_global_activa_la_puede_usar_cualquier_medico()
    {
        var template = ReminderTemplateMother.Global();

        template.IsUsableBy(DoctorId).ShouldBeTrue();
        template.IsUsableBy(OtherDoctorId).ShouldBeTrue();
    }

    [Fact]
    public void Una_plantilla_privada_activa_solo_la_usa_su_medico()
    {
        var template = ReminderTemplateMother.Create(5, DoctorId, "Seguimiento", "Asunto", "Texto");

        template.IsUsableBy(DoctorId).ShouldBeTrue();
        template.IsUsableBy(OtherDoctorId).ShouldBeFalse();
    }

    [Fact]
    public void Una_plantilla_inactiva_no_la_usa_nadie()
    {
        ReminderTemplateMother.Global(isActive: false).IsUsableBy(DoctorId).ShouldBeFalse();
        ReminderTemplateMother.Create(5, DoctorId, "Seguimiento", "Asunto", "Texto", isActive: false)
            .IsUsableBy(DoctorId).ShouldBeFalse();
    }
}
