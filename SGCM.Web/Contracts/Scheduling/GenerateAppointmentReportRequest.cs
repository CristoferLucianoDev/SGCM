using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SGCM.Data.Application.Scheduling.Commands.GenerateAppointmentReport;

namespace SGCM.Web.Contracts.Scheduling;

/// <summary>Criterios para generar el reporte de citas del médico autenticado.</summary>
/// <param name="Type">Tipo de reporte; obligatorio.</param>
/// <param name="From">Primer día del rango, local e incluido, en formato yyyy-MM-dd; obligatorio.</param>
/// <param name="To">Último día del rango, local e incluido, en formato yyyy-MM-dd; obligatorio.</param>
/// <param name="Status">Estado de cita por el que se filtra, por nombre; <c>null</c> no filtra.</param>
/// <param name="PatientName">Texto a buscar en el nombre del paciente; <c>null</c> no filtra.</param>
/// <param name="SpecialtyId">Especialidad por la que se filtra; <c>null</c> no filtra.</param>
/// <param name="SortBy">Columna de orden; por defecto la fecha.</param>
/// <param name="SortDirection">Sentido del orden; por defecto ascendente.</param>
/// <param name="PageSize">Filas de la primera página, entre 1 y 100.</param>
public sealed record GenerateAppointmentReportRequest(
    [Required] AppointmentReportType? Type,
    DateOnly? From,
    DateOnly? To,
    [property: JsonConverter(typeof(JsonStringEnumConverter<AppointmentStatusFilter>))] AppointmentStatusFilter? Status = null,
    string? PatientName = null,
    int? SpecialtyId = null,
    AppointmentReportSortField SortBy = AppointmentReportSortField.Date,
    AppointmentReportSortDirection SortDirection = AppointmentReportSortDirection.Ascending,
    int PageSize = GenerateAppointmentReportCommand.DefaultPageSize);
