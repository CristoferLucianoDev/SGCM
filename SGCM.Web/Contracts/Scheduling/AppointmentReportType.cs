using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Tipo de reporte de citas. Se envía por nombre, sin distinguir mayúsculas, por ejemplo <c>"pending"</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentReportType>))]
public enum AppointmentReportType
{
    /// <summary>Citas realizadas.</summary>
    Completed = 0,

    /// <summary>Citas canceladas o rechazadas.</summary>
    Cancelled = 1,

    /// <summary>Citas pendientes o confirmadas que aún no se atienden.</summary>
    Pending = 2,

    /// <summary>Todas las citas.</summary>
    All = 3
}
