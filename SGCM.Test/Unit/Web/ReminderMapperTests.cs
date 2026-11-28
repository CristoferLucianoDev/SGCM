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
    public void Una_vista_previa_nula_es_un_error_de_programacion()
    {
        Should.Throw<ArgumentNullException>(() => ((ReminderPreviewDto)null!).ToResponse());
    }
}
