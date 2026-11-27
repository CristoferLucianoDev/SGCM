using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Formato del archivo exportado. Se envía por nombre, sin distinguir mayúsculas, por ejemplo <c>"pdf"</c>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AppointmentReportFormat>))]
public enum AppointmentReportFormat
{
    /// <summary>Documento PDF.</summary>
    Pdf = 0,

    /// <summary>Libro de Excel (.xlsx).</summary>
    Xlsx = 1,

    /// <summary>Documento de Word (.docx).</summary>
    Docx = 2
}
