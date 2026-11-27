namespace SGCM.Data.Domain.Scheduling.Reports;

/// <summary>Acción que deja una fila en el registro de reportes.</summary>
public enum ReportAction
{
    /// <summary>El médico generó el reporte en pantalla.</summary>
    Generate,

    /// <summary>El médico exportó el reporte a un archivo.</summary>
    Export
}
