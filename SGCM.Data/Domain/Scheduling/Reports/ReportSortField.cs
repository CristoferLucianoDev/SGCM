namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>Columna por la que se ordenan las filas de un reporte de citas.</summary>
public enum ReportSortField
{
    /// <summary>Fecha y hora de inicio de la cita.</summary>
    Date,

    /// <summary>Nombre del paciente.</summary>
    Patient,

    /// <summary>Estado de la cita.</summary>
    Status
}
