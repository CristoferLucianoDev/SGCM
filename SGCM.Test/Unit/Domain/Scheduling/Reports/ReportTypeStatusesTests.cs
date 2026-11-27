using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Reports;

public sealed class ReportTypeStatusesTests
{
    private const int UndefinedType = 99;

    [Fact]
    public void Las_realizadas_son_solo_las_completadas()
    {
        ReportTypeStatuses.Of(ReportType.Completed).ShouldBe(new[] { AppointmentStatus.Completed });
    }

    [Fact]
    public void Las_canceladas_incluyen_las_rechazadas()
    {
        ReportTypeStatuses.Of(ReportType.Cancelled)
            .ShouldBe(new[] { AppointmentStatus.Cancelled, AppointmentStatus.Rejected }, ignoreOrder: true);
    }

    [Fact]
    public void Las_pendientes_incluyen_las_confirmadas_que_aun_no_se_atienden()
    {
        ReportTypeStatuses.Of(ReportType.Pending)
            .ShouldBe(new[] { AppointmentStatus.Pending, AppointmentStatus.Confirmed }, ignoreOrder: true);
    }

    [Fact]
    public void Todas_incluye_los_cinco_estados()
    {
        ReportTypeStatuses.Of(ReportType.All).ShouldBe(Enum.GetValues<AppointmentStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void Cada_estado_pertenece_a_un_solo_tipo_distinto_de_todas()
    {
        var statuses = new[] { ReportType.Completed, ReportType.Cancelled, ReportType.Pending }
            .SelectMany(ReportTypeStatuses.Of)
            .ToList();

        statuses.ShouldBe(Enum.GetValues<AppointmentStatus>(), ignoreOrder: true);
    }

    [Fact]
    public void Un_tipo_no_definido_lanza_una_excepcion()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ReportTypeStatuses.Of((ReportType)UndefinedType));
    }
}
