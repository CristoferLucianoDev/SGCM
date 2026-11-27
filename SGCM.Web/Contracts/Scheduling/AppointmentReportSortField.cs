using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Columna por la que se ordena el reporte. Se envía por nombre, por ejemplo <c>"patient"</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentReportSortField>))]
public enum AppointmentReportSortField
{
    /// <summary>Fecha y hora de la cita.</summary>
    Date = 0,

    /// <summary>Nombre del paciente.</summary>
    Patient = 1,

    /// <summary>Estado de la cita.</summary>
    Status = 2
}
