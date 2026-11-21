using SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AvailableSlotMapperTests
{
    private static readonly DateTimeOffset First = new(2027, 1, 10, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Cada_horario_se_mapea_sin_cambiar_los_datos_ni_el_orden()
    {
        IReadOnlyList<AvailableSlotDto> slots =
        [
            new(First, First.AddHours(2)),
            new(First.AddHours(1), First.AddHours(3)),
            new(First.AddHours(5), First.AddHours(7))
        ];

        var response = slots.ToResponse();

        response.Items.ShouldBe(new[]
        {
            new AvailableSlotResponse(First, First.AddHours(2)),
            new AvailableSlotResponse(First.AddHours(1), First.AddHours(3)),
            new AvailableSlotResponse(First.AddHours(5), First.AddHours(7))
        });
    }

    [Fact]
    public void Una_lista_vacia_da_Items_vacio_y_no_nulo()
    {
        IReadOnlyList<AvailableSlotDto> slots = [];

        var response = slots.ToResponse();

        response.Items.ShouldNotBeNull();
        response.Items.ShouldBeEmpty();
    }

    [Fact]
    public void Los_instantes_conservan_su_desfase_cero()
    {
        IReadOnlyList<AvailableSlotDto> slots = [new(First, First.AddHours(2))];

        var item = slots.ToResponse().Items.ShouldHaveSingleItem();

        item.StartsAt.Offset.ShouldBe(TimeSpan.Zero);
        item.EndsAt.Offset.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Una_lista_nula_se_rechaza()
    {
        IReadOnlyList<AvailableSlotDto> slots = null!;

        Should.Throw<ArgumentNullException>(() => slots.ToResponse());
    }
}
