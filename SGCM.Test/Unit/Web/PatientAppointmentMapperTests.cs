using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentDetail;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Contracts.Scheduling;
using SGCM.Web.Mappers.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class PatientAppointmentMapperTests
{
    private const string EmptyListMessage = "No tienes citas registradas.";
    private const int ConfirmationHours = 48;

    private static readonly AllowedAppointmentActions NoActions = new([], false, null, null);

    private static readonly DateOnly From = new(2027, 1, 1);
    private static readonly DateOnly To = new(2027, 3, 1);
    private static readonly DateTimeOffset StartsAt = new(2027, 1, 12, 13, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ToQuery_conserva_los_filtros_y_la_pagina()
    {
        var request = new ListPatientAppointmentsRequest(AppointmentStatusFilter.Cancelled, From, To, 3, 7);

        request.ToQuery().ShouldBe(new GetPatientAppointmentHistoryQuery(AppointmentStatus.Cancelled, From, To, 3, 7));
    }

    [Theory]
    [InlineData(AppointmentStatusFilter.Pending, AppointmentStatus.Pending)]
    [InlineData(AppointmentStatusFilter.Confirmed, AppointmentStatus.Confirmed)]
    [InlineData(AppointmentStatusFilter.Rejected, AppointmentStatus.Rejected)]
    [InlineData(AppointmentStatusFilter.Cancelled, AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatusFilter.Completed, AppointmentStatus.Completed)]
    public void ToQuery_convierte_cada_filtro_a_su_estado_del_dominio(
        AppointmentStatusFilter filter, AppointmentStatus expected)
    {
        new ListPatientAppointmentsRequest(Status: filter).ToQuery().Status.ShouldBe(expected);
    }

    [Fact]
    public void ToQuery_pasa_un_filtro_nulo_como_nulo()
    {
        var query = new ListPatientAppointmentsRequest().ToQuery();

        query.Status.ShouldBeNull();
        query.ShouldBe(new GetPatientAppointmentHistoryQuery());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    [InlineData(int.MaxValue)]
    public void ToQuery_no_convierte_filtros_no_definidos(int filter)
    {
        var exception = Should.Throw<ArgumentOutOfRangeException>(() =>
            new ListPatientAppointmentsRequest(Status: (AppointmentStatusFilter)filter).ToQuery());

        exception.ParamName.ShouldBe(nameof(ListPatientAppointmentsRequest.Status));
    }

    [Theory]
    [InlineData(AppointmentStatus.Pending, "pending")]
    [InlineData(AppointmentStatus.Confirmed, "confirmed")]
    [InlineData(AppointmentStatus.Rejected, "rejected")]
    [InlineData(AppointmentStatus.Cancelled, "cancelled")]
    [InlineData(AppointmentStatus.Completed, "completed")]
    public void ToResponse_de_la_cita_copia_los_datos_y_el_estado_en_minusculas(AppointmentStatus status, string expected)
    {
        var response = CreateAppointment(1, status).ToResponse();

        response.ShouldBe(new PatientAppointmentResponse(
            1,
            StartsAt,
            StartsAt.AddHours(2),
            new DateOnly(2027, 1, 12),
            new TimeOnly(9, 0),
            new TimeOnly(11, 0),
            3,
            "Luis Pérez",
            5,
            "Cardiología",
            expected));
    }

    [Fact]
    public void ToResponse_de_la_cita_no_convierte_estados_no_definidos()
    {
        var appointment = CreateAppointment(1, (AppointmentStatus)int.MaxValue);

        Should.Throw<ArgumentOutOfRangeException>(() => appointment.ToResponse());
    }

    [Fact]
    public void ToResponse_de_la_pagina_conserva_el_orden_los_numeros_y_el_total_sin_mensaje()
    {
        var page = new PagedResult<PatientAppointmentDto>(
            [CreateAppointment(9, AppointmentStatus.Pending), CreateAppointment(2, AppointmentStatus.Cancelled)],
            3, 2, 7);

        var response = page.ToResponse();

        response.Items.Select(item => item.AppointmentId).ShouldBe(new[] { 9, 2 });
        response.Items.Select(item => item.Status).ShouldBe(new[] { "pending", "cancelled" });
        response.PageNumber.ShouldBe(3);
        response.PageSize.ShouldBe(2);
        response.TotalCount.ShouldBe(7);
        response.Message.ShouldBeNull();
    }

    [Fact]
    public void ToResponse_de_la_pagina_sin_elementos_trae_el_mensaje()
    {
        var response = new PagedResult<PatientAppointmentDto>([], 1, 20, 0).ToResponse();

        response.Items.ShouldBeEmpty();
        response.TotalCount.ShouldBe(0);
        response.Message.ShouldBe(EmptyListMessage);
    }

    [Theory]
    [InlineData("Dolor de cabeza", "No puedo asistir")]
    [InlineData(null, null)]
    [InlineData("Dolor de cabeza", null)]
    [InlineData(null, "No puedo asistir")]
    public void ToResponse_del_detalle_conserva_la_cita_y_los_dos_motivos(string? reasonForVisit, string? statusReason)
    {
        var appointment = CreateAppointment(4, AppointmentStatus.Cancelled);

        var response = new PatientAppointmentDetailDto(appointment, reasonForVisit, statusReason, NoActions).ToResponse();

        response.Appointment.ShouldBe(appointment.ToResponse());
        response.ReasonForVisit.ShouldBe(reasonForVisit);
        response.StatusReason.ShouldBe(statusReason);
    }

    [Fact]
    public void ToResponse_del_detalle_convierte_cada_accion_a_su_texto_en_camelCase_en_el_mismo_orden()
    {
        var allowed = new AllowedAppointmentActions(
            [
                AppointmentAction.Cancel, AppointmentAction.Reschedule, AppointmentAction.ChangeDoctor,
                AppointmentAction.Confirm, AppointmentAction.Reject, AppointmentAction.Extend, AppointmentAction.Complete
            ],
            false, null, null);

        var response = new PatientAppointmentDetailDto(CreateAppointment(4, AppointmentStatus.Pending), null, null, allowed)
            .ToResponse();

        response.AllowedActions.ShouldBe(new[]
        {
            "cancel", "reschedule", "changeDoctor", "confirm", "reject", "extend", "complete"
        });
    }

    [Fact]
    public void ToResponse_del_detalle_no_convierte_acciones_no_definidas()
    {
        var allowed = new AllowedAppointmentActions([(AppointmentAction)int.MaxValue], false, null, null);
        var detail = new PatientAppointmentDetailDto(CreateAppointment(4, AppointmentStatus.Pending), null, null, allowed);

        Should.Throw<ArgumentOutOfRangeException>(() => detail.ToResponse());
    }

    [Theory]
    [InlineData(1440, 24)]
    [InlineData(119, 1)]
    [InlineData(90, 1)]
    [InlineData(0, 0)]
    public void ToResponse_del_detalle_redondea_hacia_abajo_los_minutos_que_faltan_para_confirmar(
        int seconds, int expectedMinutes)
    {
        var deadline = StartsAt.AddHours(-ConfirmationHours);
        var allowed = new AllowedAppointmentActions([], false, deadline, TimeSpan.FromSeconds(seconds));

        var response = new PatientAppointmentDetailDto(CreateAppointment(4, AppointmentStatus.Pending), null, null, allowed)
            .ToResponse();

        response.ConfirmationDeadline.ShouldBe(deadline);
        response.MinutesUntilConfirmationDeadline.ShouldBe(expectedMinutes);
    }

    [Fact]
    public void ToResponse_del_detalle_sin_plazo_de_confirmacion_deja_ambos_datos_nulos()
    {
        var allowed = new AllowedAppointmentActions([], false, null, null);

        var response = new PatientAppointmentDetailDto(CreateAppointment(4, AppointmentStatus.Confirmed), null, null, allowed)
            .ToResponse();

        response.ConfirmationDeadline.ShouldBeNull();
        response.MinutesUntilConfirmationDeadline.ShouldBeNull();
        response.AllowedActions.ShouldBeEmpty();
    }

    [Fact]
    public void Los_argumentos_nulos_lanzan_ArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => ((ListPatientAppointmentsRequest)null!).ToQuery());
        Should.Throw<ArgumentNullException>(() => ((PatientAppointmentDto)null!).ToResponse());
        Should.Throw<ArgumentNullException>(() => ((PagedResult<PatientAppointmentDto>)null!).ToResponse());
        Should.Throw<ArgumentNullException>(() => ((PatientAppointmentDetailDto)null!).ToResponse());
    }

    private static PatientAppointmentDto CreateAppointment(int id, AppointmentStatus status) => new(
        id,
        StartsAt,
        StartsAt.AddHours(2),
        new DateOnly(2027, 1, 12),
        new TimeOnly(9, 0),
        new TimeOnly(11, 0),
        3,
        "Luis Pérez",
        5,
        "Cardiología",
        status);
}
