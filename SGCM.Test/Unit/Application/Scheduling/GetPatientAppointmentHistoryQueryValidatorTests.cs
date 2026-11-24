using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Scheduling.Appointments;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetPatientAppointmentHistoryQueryValidatorTests
{
    private static readonly DateOnly January = new(2027, 1, 1);
    private static readonly DateOnly March = new(2027, 3, 1);

    private readonly GetPatientAppointmentHistoryQueryValidator _validator = new();

    [Fact]
    public void La_consulta_por_defecto_es_valida_y_trae_la_primera_pagina_de_20()
    {
        var query = new GetPatientAppointmentHistoryQuery();

        _validator.Validate(query).IsValid.ShouldBeTrue();
        query.PageNumber.ShouldBe(1);
        query.PageSize.ShouldBe(20);
    }

    [Fact]
    public void Una_consulta_con_todos_los_filtros_es_valida()
    {
        var query = new GetPatientAppointmentHistoryQuery(AppointmentStatus.Cancelled, January, March, 2, 50);

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_rango_con_la_misma_fecha_inicial_y_final_es_valido()
    {
        _validator.Validate(new GetPatientAppointmentHistoryQuery(From: January, To: January)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Solo_la_fecha_inicial_o_solo_la_final_es_valido()
    {
        _validator.Validate(new GetPatientAppointmentHistoryQuery(From: January)).IsValid.ShouldBeTrue();
        _validator.Validate(new GetPatientAppointmentHistoryQuery(To: March)).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void El_numero_de_pagina_debe_ser_al_menos_1(int pageNumber)
    {
        AssertSingleError(new GetPatientAppointmentHistoryQuery(PageNumber: pageNumber), "Appointment.InvalidPageNumber");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void El_tamano_de_pagina_debe_estar_entre_1_y_100(int pageSize)
    {
        AssertSingleError(new GetPatientAppointmentHistoryQuery(PageSize: pageSize), "Appointment.InvalidPageSize");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(100)]
    public void Los_limites_del_tamano_de_pagina_son_validos(int pageSize)
    {
        _validator.Validate(new GetPatientAppointmentHistoryQuery(PageSize: pageSize)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_desplazamiento_que_no_cabe_en_un_entero_se_rechaza()
    {
        AssertSingleError(
            new GetPatientAppointmentHistoryQuery(PageNumber: int.MaxValue, PageSize: 100), "Appointment.PageOffsetTooLarge");
    }

    [Fact]
    public void Un_estado_no_definido_se_rechaza()
    {
        AssertSingleError(
            new GetPatientAppointmentHistoryQuery(Status: (AppointmentStatus)99), "Appointment.InvalidStatus");
    }

    [Fact]
    public void Una_fecha_inicial_posterior_a_la_final_se_rechaza()
    {
        AssertSingleError(
            new GetPatientAppointmentHistoryQuery(From: March, To: January), "Appointment.InvalidDateRange");
    }

    [Theory]
    [InlineData(2000, 1, 1)]
    [InlineData(2100, 12, 31)]
    public void Una_fecha_en_el_borde_del_rango_de_filtro_es_valida(int year, int month, int day)
    {
        var date = new DateOnly(year, month, day);

        _validator.Validate(new GetPatientAppointmentHistoryQuery(From: date)).IsValid.ShouldBeTrue();
        _validator.Validate(new GetPatientAppointmentHistoryQuery(To: date)).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Un_rango_que_cubre_los_dos_bordes_del_filtro_es_valido()
    {
        var query = new GetPatientAppointmentHistoryQuery(From: new DateOnly(2000, 1, 1), To: new DateOnly(2100, 12, 31));

        _validator.Validate(query).IsValid.ShouldBeTrue();
    }

    [Theory]
    [InlineData(1999, 12, 31)]
    [InlineData(1950, 6, 15)]
    public void Una_fecha_inicial_antes_del_rango_de_filtro_se_rechaza(int year, int month, int day)
    {
        AssertSingleError(new GetPatientAppointmentHistoryQuery(From: new DateOnly(year, month, day)), "Appointment.DateOutOfRange");
    }

    [Theory]
    [InlineData(2101, 1, 1)]
    [InlineData(2200, 3, 10)]
    public void Una_fecha_final_despues_del_rango_de_filtro_se_rechaza(int year, int month, int day)
    {
        AssertSingleError(new GetPatientAppointmentHistoryQuery(To: new DateOnly(year, month, day)), "Appointment.DateOutOfRange");
    }

    [Fact]
    public void El_mensaje_de_fecha_fuera_de_rango_indica_el_rango_permitido()
    {
        var result = _validator.Validate(new GetPatientAppointmentHistoryQuery(From: new DateOnly(1999, 12, 31)));

        result.Errors.ShouldHaveSingleItem().ErrorMessage
            .ShouldBe("Las fechas del filtro deben estar entre 01/01/2000 y 31/12/2100.");
    }

    private void AssertSingleError(GetPatientAppointmentHistoryQuery query, string code)
    {
        var result = _validator.Validate(query);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldHaveSingleItem().ErrorCode.ShouldBe(code);
    }
}
