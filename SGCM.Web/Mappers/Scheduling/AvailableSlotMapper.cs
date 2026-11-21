using SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;
using SGCM.Web.Contracts.Scheduling;

namespace SGCM.Web.Mappers.Scheduling;

/// <summary>Convierte el resultado de la consulta de horarios libres en su contrato de la API.</summary>
public static class AvailableSlotMapper
{
    /// <summary>Proyecta cada horario sin cambiar sus datos ni el orden; una lista vacía da un <c>Items</c> vacío.</summary>
    /// <param name="slots">Horarios libres que devolvió la consulta.</param>
    /// <returns>La respuesta de la API.</returns>
    /// <exception cref="ArgumentNullException">Si <paramref name="slots"/> es nulo.</exception>
    public static AvailableSlotsResponse ToResponse(this IReadOnlyList<AvailableSlotDto> slots)
    {
        ArgumentNullException.ThrowIfNull(slots);

        return new AvailableSlotsResponse(
            slots.Select(slot => new AvailableSlotResponse(slot.StartsAt, slot.EndsAt)).ToArray());
    }
}
