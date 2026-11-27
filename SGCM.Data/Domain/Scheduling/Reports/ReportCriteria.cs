using System.Text.Json;
using System.Text.Json.Serialization;
using SGCM.Data.Domain.Scheduling.Appointments;

namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>
/// Criterios de un reporte de citas: tipo, rango de fechas obligatorio, filtros opcionales y orden. Los comparten
/// la generación, el filtrado y la exportación, para que los tres muestren exactamente las mismas filas.
/// </summary>
/// <param name="Type">Tipo de reporte.</param>
/// <param name="From">Primer día del rango en el calendario de la agenda, incluido.</param>
/// <param name="To">Último día del rango en el calendario de la agenda, incluido.</param>
/// <param name="Status">Estado de cita por el que se filtra, o <see langword="null"/> para no filtrar.</param>
/// <param name="PatientName">Texto a buscar en el nombre del paciente, o <see langword="null"/> para no filtrar.</param>
/// <param name="SpecialtyId">Especialidad por la que se filtra, o <see langword="null"/> para no filtrar.</param>
/// <param name="SortBy">Columna por la que se ordena.</param>
/// <param name="SortDirection">Sentido del orden.</param>
public sealed record ReportCriteria(
    ReportType Type,
    DateOnly From,
    DateOnly To,
    AppointmentStatus? Status = null,
    string? PatientName = null,
    int? SpecialtyId = null,
    ReportSortField SortBy = ReportSortField.Date,
    ReportSortDirection SortDirection = ReportSortDirection.Ascending)
{
    /// <summary>
    /// Longitud máxima del texto de búsqueda por paciente: nombre y apellido de 100 caracteres cada uno,
    /// separados por un espacio, para poder buscar por el nombre completo.
    /// </summary>
    public const int PatientNameMaxLength = 201;

    // Una sola instancia: crear JsonSerializerOptions en cada llamada descarta su caché de metadatos.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    /// <summary>
    /// Serializa los criterios para la columna <c>filters</c> del registro de reportes, con nombres y valores en
    /// snake_case como el resto de la base y sin los filtros que no se aplicaron.
    /// </summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
}
