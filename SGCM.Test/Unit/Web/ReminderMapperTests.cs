using SGCM.Data.Application.Messaging.Commands.ScheduleReminder;
using SGCM.Data.Application.Messaging.Queries.GetReminderPreview;
using SGCM.Web.Contracts.Messaging;
using SGCM.Web.Mappers.Messaging;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class ReminderMapperTests
{
    [Fact]
    public void La_vista_previa_pasa_a_la_respuesta_sin_cambios()
    {
        new ReminderPreviewDto(1, "Asunto", "Hola Ana").ToResponse()
            .ShouldBe(new ReminderPreviewResponse(1, "Asunto", "Hola Ana"));
    }

    [Fact]
    public void La_peticion_pasa_al_comando_con_el_id_de_la_cita()
    {
        var sendAt = new DateTimeOffset(2027, 1, 11, 9, 0, 0, TimeSpan.FromHours(-4));

        new ScheduleReminderRequest("Hola Ana", sendAt, 1).ToCommand(99)
            .ShouldBe(new ScheduleReminderCommand(99, 1, "Hola Ana", sendAt));
        new ScheduleReminderRequest().ToCommand(99).ShouldBe(new ScheduleReminderCommand(99, null, null, null));
    }

    [Fact]
    public void El_recordatorio_programado_pasa_a_la_respuesta_sin_cambios()
    {
        var sendAt = new DateTimeOffset(2027, 1, 11, 13, 0, 0, TimeSpan.Zero);

        new ScheduledReminderDto(700, 99, null, "Hola Ana", sendAt).ToResponse()
            .ShouldBe(new ScheduledReminderResponse(700, 99, null, "Hola Ana", sendAt));
    }

    [Fact]
    public void Valores_nulos_son_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => ((ReminderPreviewDto)null!).ToResponse());
        Should.Throw<ArgumentNullException>(() => ((ScheduleReminderRequest)null!).ToCommand(99));
        Should.Throw<ArgumentNullException>(() => ((ScheduledReminderDto)null!).ToResponse());
    }
}
