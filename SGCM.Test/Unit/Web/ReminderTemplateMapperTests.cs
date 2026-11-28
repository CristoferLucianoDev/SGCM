using SGCM.Data.Application.Messaging.Commands.CreateReminderTemplate;
using SGCM.Data.Application.Messaging.Commands.UpdateReminderTemplate;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Web.Contracts.Messaging;
using SGCM.Web.Mappers.Messaging;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class ReminderTemplateMapperTests
{
    [Fact]
    public void La_peticion_pasa_a_los_comandos_de_crear_y_editar()
    {
        var request = new ReminderTemplateRequest("Nombre", "Asunto", "Cuerpo");

        request.ToCreateCommand().ShouldBe(new CreateReminderTemplateCommand("Nombre", "Asunto", "Cuerpo"));
        request.ToUpdateCommand(5).ShouldBe(new UpdateReminderTemplateCommand(5, "Nombre", "Asunto", "Cuerpo"));
    }

    [Fact]
    public void La_plantilla_pasa_a_la_respuesta_sin_cambios()
    {
        new ReminderTemplateDto(1, "Recordatorio de cita", "Asunto", "Cuerpo", true, true).ToResponse()
            .ShouldBe(new ReminderTemplateResponse(1, "Recordatorio de cita", "Asunto", "Cuerpo", true, true));
    }

    [Fact]
    public void Valores_nulos_son_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => ((ReminderTemplateRequest)null!).ToCreateCommand());
        Should.Throw<ArgumentNullException>(() => ((ReminderTemplateRequest)null!).ToUpdateCommand(5));
        Should.Throw<ArgumentNullException>(() => ((ReminderTemplateDto)null!).ToResponse());
    }
}
