using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorCalendar;
using SGCM.Data.Application.Scheduling.Queries.GetDoctorSchedule;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetDoctorScheduleQueryTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;
    private static readonly DateOnly January1 = new(2027, 1, 1);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IDoctorScheduleReader _reader = Substitute.For<IDoctorScheduleReader>();
    private readonly GetDoctorScheduleQueryValidator _validator = new();
    private readonly GetDoctorCalendarQueryValidator _calendarValidator = new();
    private readonly PagedResult<DoctorScheduleItemDto> _page = new([], 1, 20, 0);

    public GetDoctorScheduleQueryTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(DoctorId);
        _reader.ListAsync(Arg.Any<int>(), Arg.Any<DoctorScheduleFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_page);
    }

    [Fact]
    public async Task Pasa_al_lector_el_medico_de_la_sesion_los_filtros_combinados_y_la_pagina()
    {
        var query = new GetDoctorScheduleQuery(
            AppointmentStatus.Confirmed, January1, January1.AddDays(6), "  maria  ", DoctorScheduleSort.Descending, 2, 10);

        var result = await Handle(query);

        result.Value.ShouldBeSameAs(_page);
        await _reader.Received(1).ListAsync(
            DoctorId,
            new DoctorScheduleFilter(AppointmentStatus.Confirmed, January1, January1.AddDays(6), "maria", DoctorScheduleSort.Descending),
            2, 10, CancellationToken.None);
    }

    [Fact]
    public async Task Sin_filtros_ordena_ascendente_desde_la_primera_pagina()
    {
        await Handle(new GetDoctorScheduleQuery(PatientName: "   "));

        await _reader.Received(1).ListAsync(
            DoctorId, new DoctorScheduleFilter(null, null, null, null, DoctorScheduleSort.Ascending), 1, 20, CancellationToken.None);
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_activo_no_tiene_agenda()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);
        (await Handle(new GetDoctorScheduleQuery())).Error.ShouldBe(DoctorErrors.NotFound);

        _currentUser.IsInRole(UserRole.Doctor).Returns(false);
        (await Handle(new GetDoctorScheduleQuery())).Error.ShouldBe(DoctorErrors.NotFound);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_consulta_invalida_se_rechaza_sin_leer()
    {
        (await Handle(new GetDoctorScheduleQuery(PageSize: 0))).Error.Code.ShouldBe("Appointment.InvalidPageSize");
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending, true)]
    [InlineData(AppointmentStatus.Confirmed, true)]
    [InlineData(AppointmentStatus.Cancelled, false)]
    [InlineData(AppointmentStatus.Rejected, false)]
    [InlineData(AppointmentStatus.Completed, false)]
    public void La_agenda_solo_filtra_por_estados_activos(AppointmentStatus status, bool valid)
    {
        var result = _validator.Validate(new GetDoctorScheduleQuery(Status: status));

        result.IsValid.ShouldBe(valid);
        if (!valid) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.InvalidScheduleStatus");
    }

    [Fact]
    public void Valida_pagina_orden_fechas_y_nombre()
    {
        Code(new GetDoctorScheduleQuery(PageNumber: 0)).ShouldBe("Appointment.InvalidPageNumber");
        Code(new GetDoctorScheduleQuery(PageSize: 101)).ShouldBe("Appointment.InvalidPageSize");
        Code(new GetDoctorScheduleQuery(Sort: (DoctorScheduleSort)9)).ShouldBe("Appointment.InvalidSort");
        Code(new GetDoctorScheduleQuery(From: January1, To: January1.AddDays(-1))).ShouldBe("Appointment.InvalidDateRange");
        Code(new GetDoctorScheduleQuery(From: new DateOnly(1999, 12, 31))).ShouldBe("Appointment.DateOutOfRange");
        Code(new GetDoctorScheduleQuery(PatientName: new string('A', GetDoctorScheduleQuery.PatientNameMaxLength + 1)))
            .ShouldBe("Appointment.PatientNameTooLong");
        _validator.Validate(new GetDoctorScheduleQuery(PatientName: new string('A', GetDoctorScheduleQuery.PatientNameMaxLength)))
            .IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(30, true)]
    [InlineData(31, false)]
    public void El_calendario_abarca_como_maximo_31_dias(int extraDays, bool valid)
    {
        var result = _calendarValidator.Validate(new GetDoctorCalendarQuery(January1, January1.AddDays(extraDays)));

        result.IsValid.ShouldBe(valid);
        if (!valid) result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.DateRangeTooLong");
    }

    [Fact]
    public void El_calendario_exige_el_rango_en_orden()
    {
        _calendarValidator.Validate(new GetDoctorCalendarQuery(January1, January1.AddDays(-1))).Errors
            .ShouldHaveSingleItem().ErrorCode.ShouldBe("Appointment.InvalidDateRange");
    }

    [Fact]
    public async Task El_calendario_devuelve_todas_las_citas_del_rango_sin_paginar()
    {
        IReadOnlyList<DoctorScheduleItemDto> items = [];
        _reader.ListRangeAsync(DoctorId, January1, January1.AddDays(30), Arg.Any<CancellationToken>()).Returns(items);

        var result = await new GetDoctorCalendarQueryHandler(_currentUser, _doctors, _reader, _calendarValidator)
            .HandleAsync(new GetDoctorCalendarQuery(January1, January1.AddDays(30)), CancellationToken.None);

        result.Value.ShouldBeSameAs(items);
    }

    [Fact]
    public async Task El_calendario_de_un_rango_invalido_o_sin_medico_no_lee()
    {
        var handler = new GetDoctorCalendarQueryHandler(_currentUser, _doctors, _reader, _calendarValidator);
        (await handler.HandleAsync(new GetDoctorCalendarQuery(January1, January1.AddDays(31)), CancellationToken.None))
            .Error.Code.ShouldBe("Appointment.DateRangeTooLong");

        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);
        (await handler.HandleAsync(new GetDoctorCalendarQuery(January1, January1), CancellationToken.None))
            .Error.ShouldBe(DoctorErrors.NotFound);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private string Code(GetDoctorScheduleQuery query) => _validator.Validate(query).Errors.ShouldHaveSingleItem().ErrorCode;

    private Task<Result<PagedResult<DoctorScheduleItemDto>>> Handle(GetDoctorScheduleQuery query) =>
        new GetDoctorScheduleQueryHandler(_currentUser, _doctors, _reader, _validator).HandleAsync(query, CancellationToken.None);
}
