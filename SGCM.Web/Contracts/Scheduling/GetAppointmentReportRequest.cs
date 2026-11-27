using System.ComponentModel.DataAnnotations;
using SGCM.Data.Application.Scheduling.Queries.GetAppointmentReport;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>
/// Parámetros de consulta para filtrar y ordenar el reporte de citas del médico autenticado. Los tipos, estados y el
/// orden se envían por nombre, sin distinguir mayúsculas, por ejemplo <c>?type=pending&amp;sortBy=patient</c>.
/// </summary>
/// <param name="Type">Tipo de reporte; obligatorio.</param>
/// <param name="From">Primer día del rango, local e incluido, en formato yyyy-MM-dd; obligatorio.</param>
/// <param name="To">Último día del rango, local e incluido, en formato yyyy-MM-dd; obligatorio.</param>
/// <param name="Status">Estado de cita por el que se filtra; <c>null</c> no filtra.</param>
/// <param name="PatientName">Texto a buscar en el nombre del paciente; <c>null</c> no filtra.</param>
/// <param name="SpecialtyId">Especialidad por la que se filtra; <c>null</c> no filtra.</param>
/// <param name="SortBy">Columna de orden; por defecto la fecha.</param>
/// <param name="SortDirection">Sentido del orden; por defecto ascendente.</param>
/// <param name="PageNumber">Número de página, desde 1.</param>
/// <param name="PageSize">Filas por página, entre 1 y 100.</param>
public sealed record GetAppointmentReportRequest(
    [Required] AppointmentReportType? Type,
    DateOnly? From,
    DateOnly? To,
    AppointmentStatusFilter? Status = null,
    string? PatientName = null,
    int? SpecialtyId = null,
    AppointmentReportSortField SortBy = AppointmentReportSortField.Date,
    AppointmentReportSortDirection SortDirection = AppointmentReportSortDirection.Ascending,
    int PageNumber = GetAppointmentReportQuery.FirstPage,
    int PageSize = GetAppointmentReportQuery.DefaultPageSize);
