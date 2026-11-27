using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Reports;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class AppointmentReportBuilderTests
{
    private const int UserId = 15;
    private const int DoctorId = 7;
    private const int PageNumber = 3;
    private const int PageSize = 10;
    private const int ExpectedOffset = 20;
    private const string TimeZone = "America/Santo_Domingo";

    // El 1 de enero a medianoche en Santo Domingo son las 04:00 UTC; el rango termina al empezar el 1 de febrero.
    private static readonly DateTimeOffset FromUtc = new(2027, 1, 1, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ToUtcExclusive = new(2027, 2, 1, 4, 0, 0, TimeSpan.Zero);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctorReader = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentReportReader _reader = Substitute.For<IAppointmentReportReader>();

    public AppointmentReportBuilderTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctorReader.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)DoctorId);
        GivenCounts(new Dictionary<AppointmentStatus, int>());
        GivenRows();
    }

    [Fact]
    public async Task Un_usuario_no_autenticado_recibe_DoctorOnly_y_no_se_lee_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await CreateBuilder().BuildPageAsync(ReportCriteriaMother.Valid(), PageNumber, PageSize, CancellationToken.None);

        result.Error.ShouldBe(ReportErrors.DoctorOnly);
        await _reader.DidNotReceiveWithAnyArgs().CountByStatusAsync(default!, default);
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_recibe_DoctorOnly()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        var result = await CreateBuilder().BuildAllAsync(ReportCriteriaMother.Valid(), CancellationToken.None);

        result.Error.ShouldBe(ReportErrors.DoctorOnly);
        await _reader.DidNotReceiveWithAnyArgs().ListAsync(default!, default, default, default, default, default);
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_recibe_NotFound()
    {
        _doctorReader.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await CreateBuilder().BuildPageAsync(ReportCriteriaMother.Valid(), PageNumber, PageSize, CancellationToken.None);

        result.Error.ShouldBe(DoctorErrors.NotFound);
    }

    [Fact]
    public async Task Filtra_por_el_medico_autenticado_los_estados_del_tipo_y_el_rango_local_convertido_a_UTC()
    {
        var criteria = ReportCriteriaMother.Valid() with { Type = ReportType.Pending, SpecialtyId = 4 };

        var result = await CreateBuilder().BuildPageAsync(criteria, PageNumber, PageSize, CancellationToken.None);

        result.Value.DoctorId.ShouldBe(DoctorId);
        await _reader.Received(1).CountByStatusAsync(
            Arg.Is<AppointmentReportFilter>(filter => filter.DoctorId == DoctorId
                && filter.Statuses.SequenceEqual(new[] { AppointmentStatus.Pending, AppointmentStatus.Confirmed })
                && filter.FromUtc == FromUtc
                && filter.ToUtcExclusive == ToUtcExclusive
                && filter.PatientName == null
                && filter.SpecialtyId == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_estado_dentro_del_tipo_reduce_el_filtro_a_ese_estado()
    {
        var criteria = ReportCriteriaMother.Valid() with { Type = ReportType.Pending, Status = AppointmentStatus.Confirmed };

        await CreateBuilder().BuildPageAsync(criteria, PageNumber, PageSize, CancellationToken.None);

        await _reader.Received(1).CountByStatusAsync(
            Arg.Is<AppointmentReportFilter>(filter => filter.Statuses.SequenceEqual(new[] { AppointmentStatus.Confirmed })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_estado_fuera_del_tipo_deja_el_filtro_sin_estados()
    {
        var criteria = ReportCriteriaMother.Valid() with { Type = ReportType.Completed, Status = AppointmentStatus.Pending };

        await CreateBuilder().BuildPageAsync(criteria, PageNumber, PageSize, CancellationToken.None);

        await _reader.Received(1).CountByStatusAsync(
            Arg.Is<AppointmentReportFilter>(filter => filter.Statuses.Count == 0), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("  ana  ", "ana")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public async Task El_nombre_del_paciente_se_recorta_y_en_blanco_no_filtra(string patientName, string? expected)
    {
        var criteria = ReportCriteriaMother.Valid() with { PatientName = patientName };

        await CreateBuilder().BuildPageAsync(criteria, PageNumber, PageSize, CancellationToken.None);

        await _reader.Received(1).CountByStatusAsync(
            Arg.Is<AppointmentReportFilter>(filter => filter.PatientName == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Los_totales_incluyen_los_cinco_estados_con_cero_y_el_total_de_filas_es_su_suma()
    {
        GivenCounts(new Dictionary<AppointmentStatus, int>
        {
            [AppointmentStatus.Pending] = 3,
            [AppointmentStatus.Completed] = 5
        });

        var result = await CreateBuilder().BuildPageAsync(ReportCriteriaMother.Valid(), PageNumber, PageSize, CancellationToken.None);

        result.Value.TotalsByStatus.ShouldBe(new Dictionary<AppointmentStatus, int>
        {
            [AppointmentStatus.Pending] = 3,
            [AppointmentStatus.Confirmed] = 0,
            [AppointmentStatus.Rejected] = 0,
            [AppointmentStatus.Cancelled] = 0,
            [AppointmentStatus.Completed] = 5
        }, ignoreOrder: true);
        result.Value.Rows.TotalCount.ShouldBe(8);
    }

    [Fact]
    public async Task En_pantalla_salta_las_paginas_anteriores_y_pide_solo_su_tamano_con_el_orden_de_los_criterios()
    {
        var criteria = ReportCriteriaMother.Valid() with
        {
            SortBy = ReportSortField.Patient,
            SortDirection = ReportSortDirection.Descending
        };

        var result = await CreateBuilder().BuildPageAsync(criteria, PageNumber, PageSize, CancellationToken.None);

        await _reader.Received(1).ListAsync(
            Arg.Any<AppointmentReportFilter>(), ReportSortField.Patient, ReportSortDirection.Descending,
            ExpectedOffset, PageSize, Arg.Any<CancellationToken>());
        result.Value.Rows.PageNumber.ShouldBe(PageNumber);
        result.Value.Rows.PageSize.ShouldBe(PageSize);
    }

    [Fact]
    public async Task Las_filas_salen_en_hora_local_con_el_nombre_completo_del_paciente()
    {
        // 13:00 UTC del 12 de enero son las 09:00 locales en Santo Domingo.
        GivenRows(new AppointmentReportRowData(
            new DateTimeOffset(2027, 1, 12, 13, 0, 0, TimeSpan.Zero), " Ana ", " Pérez ", "Cardiología",
            AppointmentStatus.Confirmed, "Control anual"));

        var result = await CreateBuilder().BuildPageAsync(ReportCriteriaMother.Valid(), PageNumber, PageSize, CancellationToken.None);

        result.Value.Rows.Items.ShouldHaveSingleItem().ShouldBe(new AppointmentReportRow(
            new DateOnly(2027, 1, 12), new TimeOnly(9, 0), "Ana Pérez", "Cardiología",
            AppointmentStatus.Confirmed, "Control anual"));
    }

    [Fact]
    public async Task Para_exportar_lee_todas_las_filas_con_el_mismo_filtro_y_orden_en_una_sola_pagina()
    {
        GivenCounts(new Dictionary<AppointmentStatus, int> { [AppointmentStatus.Cancelled] = 2 });
        GivenRows(Row(AppointmentStatus.Cancelled), Row(AppointmentStatus.Cancelled));
        var criteria = ReportCriteriaMother.Valid() with { SortBy = ReportSortField.Status };

        var result = await CreateBuilder().BuildAllAsync(criteria, CancellationToken.None);

        await _reader.Received(1).ListAsync(
            Arg.Is<AppointmentReportFilter>(filter => filter.DoctorId == DoctorId && filter.FromUtc == FromUtc),
            ReportSortField.Status, ReportSortDirection.Ascending, 0, null, Arg.Any<CancellationToken>());
        result.Value.Rows.Items.Count.ShouldBe(2);
        result.Value.Rows.PageNumber.ShouldBe(1);
        result.Value.Rows.PageSize.ShouldBe(2);
        result.Value.Rows.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Un_reporte_sin_citas_devuelve_filas_vacias_y_totales_en_cero()
    {
        var result = await CreateBuilder().BuildAllAsync(ReportCriteriaMother.Valid(), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Rows.Items.ShouldBeEmpty();
        result.Value.Rows.TotalCount.ShouldBe(0);
        result.Value.TotalsByStatus.Values.ShouldAllBe(total => total == 0);
    }

    private AppointmentReportBuilder CreateBuilder() =>
        new(_currentUser, _doctorReader, _reader, BusinessCalendar.ForTimeZone(TimeZone));

    private void GivenCounts(Dictionary<AppointmentStatus, int> counts) =>
        _reader.CountByStatusAsync(Arg.Any<AppointmentReportFilter>(), Arg.Any<CancellationToken>())
            .Returns(counts);

    private void GivenRows(params AppointmentReportRowData[] rows) =>
        _reader.ListAsync(
                Arg.Any<AppointmentReportFilter>(), Arg.Any<ReportSortField>(), Arg.Any<ReportSortDirection>(),
                Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<CancellationToken>())
            .Returns(rows);

    private static AppointmentReportRowData Row(AppointmentStatus status) =>
        new(new DateTimeOffset(2027, 1, 12, 13, 0, 0, TimeSpan.Zero), "Ana", "Pérez", "Cardiología", status, null);
}
