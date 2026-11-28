using SGCM.Data.Application.Messaging.Commands.DeactivateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

/// <summary>Los validadores de editar y desactivar solo exigen un id mayor que cero; los datos los valida el agregado.</summary>
public sealed class ReminderTemplateCommandValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_id_debe_ser_mayor_que_cero(int id)
    {
        new UpdateReminderTemplateCommandValidator().Validate(new UpdateReminderTemplateCommand(id, "N", "A", "C"))
            .Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("ReminderTemplate.InvalidId");
        new DeactivateReminderTemplateCommandValidator().Validate(new DeactivateReminderTemplateCommand(id))
            .Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("ReminderTemplate.InvalidId");
    }

    [Fact]
    public void Un_id_valido_se_acepta_aunque_los_datos_vengan_vacios()
    {
        new UpdateReminderTemplateCommandValidator().Validate(new UpdateReminderTemplateCommand(5, null, null, null))
            .IsValid.ShouldBeTrue();
        new DeactivateReminderTemplateCommandValidator().Validate(new DeactivateReminderTemplateCommand(5)).IsValid.ShouldBeTrue();
    }
}
