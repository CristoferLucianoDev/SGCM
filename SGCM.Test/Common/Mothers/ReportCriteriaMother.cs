using SGCM.Data.Domain.Scheduling.Reports;

namespace SGCM.Test.Common.Mothers;

/// <summary>
/// Object Mother de <see cref="ReportCriteria"/>: todas las citas de enero de 2027, sin filtros y ordenadas por
/// fecha ascendente.
/// </summary>
public static class ReportCriteriaMother
{
    public static readonly DateOnly From = new(2027, 1, 1);
    public static readonly DateOnly To = new(2027, 1, 31);

    public static ReportCriteria Valid() => new(ReportType.All, From, To);
}
