using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;

namespace SGCM.Data.Application.Scheduling.Queries.GetDoctorCalendar;

/// <summary>
/// Todas las citas activas del médico autenticado entre dos fechas locales, ambas incluidas, sin paginar, para que el
/// calendario muestre el día, la semana o el mes completos.
/// </summary>
public sealed record GetDoctorCalendarQuery(DateOnly From, DateOnly To) : IQuery<IReadOnlyList<DoctorScheduleItemDto>>
{
    /// <summary>Días que abarca la consulta como máximo, contando el primero y el último.</summary>
    public const int MaximumDays = 31;
}
