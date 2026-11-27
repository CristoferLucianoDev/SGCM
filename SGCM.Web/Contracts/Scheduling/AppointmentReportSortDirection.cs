using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Sentido del orden del reporte. Se envía por nombre, por ejemplo <c>"descending"</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentReportSortDirection>))]
public enum AppointmentReportSortDirection
{
    /// <summary>De menor a mayor.</summary>
    Ascending = 0,

    /// <summary>De mayor a menor.</summary>
    Descending = 1
}
