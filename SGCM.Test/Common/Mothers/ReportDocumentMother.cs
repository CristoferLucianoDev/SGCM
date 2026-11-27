using SGCM.Data.Application.Abstractions.Reporting;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="ReportDocument"/>: el reporte de enero de 2027 generado el 12 de enero a las 09:30, con
/// las seis columnas del reporte de citas, tildes y eñes en los textos y los cinco totales en orden.
/// </summary>
public static class ReportDocumentMother
{
    public const string Title = "Reporte de citas: todas";
    public static readonly DateOnly From = new(2027, 1, 1);
    public static readonly DateOnly To = new(2027, 1, 31);
    public static readonly DateTimeOffset GeneratedAt = new(2027, 1, 12, 9, 30, 0, TimeSpan.FromHours(-4));

    public static readonly IReadOnlyList<string> Columns =
        ["Fecha", "Hora", "Paciente", "Especialidad", "Estado", "Motivo de consulta"];

    public static ReportDocument WithRows(int count) => new(
        Title, From, To, GeneratedAt, Columns,
        Enumerable.Range(1, count).Select(Row).ToList(),
        new Dictionary<string, int>
        {
            ["Pendiente"] = count,
            ["Confirmada"] = 0,
            ["Rechazada"] = 0,
            ["Cancelada"] = 0,
            ["Completada"] = 0
        });

    public static IReadOnlyList<string> Row(int number) =>
        [$"{number:00}/01/2027", "09:00", $"José Núñez {number}", "Cardiología", "Pendiente", "Revisión anual"];
}
