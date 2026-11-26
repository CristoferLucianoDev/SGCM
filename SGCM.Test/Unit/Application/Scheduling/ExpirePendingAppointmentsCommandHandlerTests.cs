using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Application.Scheduling.Commands.ExpirePendingAppointments;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class ExpirePendingAppointmentsCommandHandlerTests
{
    private const int FirstId = 11;
    private const int SecondId = 12;
    private const int MissingId = 13;
    private const int HoursBeforeStartPastDeadline = 47;
    private const int HoursBeforeStartInsideDeadline = 72;
    private const int ExpectedBatchSize = 100;

    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly ExpirePendingAppointmentsCommandHandler _handler;

    public ExpirePendingAppointmentsCommandHandlerTests()
    {
        _clock.UtcNow = AppointmentMother.StartsAt.AddHours(-HoursBeforeStartPastDeadline);
        var options = Options.Create(new SchedulingOptions
        {
            ConfirmationDeadlineHours = AppointmentMother.ConfirmationDeadlineHours
        });
        _handler = new ExpirePendingAppointmentsCommandHandler(_appointments, _clock, options);
    }

    [Fact]
    public async Task Vence_las_candidatas_pendientes_y_emite_el_evento_de_cada_una()
    {
        var first = Candidate(FirstId, AppointmentStatus.Pending);
        var second = Candidate(SecondId, AppointmentStatus.Pending);

        var result = await Handle(FirstId, SecondId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
        foreach (var appointment in new[] { first, second })
        {
            appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
            appointment.StatusReason.ShouldBe(Appointment.ExpiredReason);
            appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentExpiredEvent>()
                .Appointment.ShouldBeSameAs(appointment);
        }
    }

    [Fact]
    public async Task Pasa_al_repositorio_el_instante_del_reloj_las_horas_de_la_configuracion_el_tamano_del_lote_y_el_token()
    {
        using var source = new CancellationTokenSource();
        _appointments.ListExpirableIdsAsync(default, default, default, default).ReturnsForAnyArgs(Array.Empty<int>());

        await _handler.HandleAsync(new ExpirePendingAppointmentsCommand(), source.Token);

        await _appointments.Received(1).ListExpirableIdsAsync(
            _clock.UtcNow, AppointmentMother.ConfirmationDeadlineHours, ExpectedBatchSize, source.Token);
    }

    [Fact]
    public async Task Una_candidata_que_ya_se_confirmo_no_se_cuenta_ni_cambia()
    {
        var confirmed = Candidate(FirstId, AppointmentStatus.Confirmed);

        var result = await Handle(FirstId);

        result.Value.ShouldBe(0);
        confirmed.Status.ShouldBe(AppointmentStatus.Confirmed);
        confirmed.DomainEvents.OfType<AppointmentExpiredEvent>().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_candidata_todavia_en_plazo_no_se_cuenta_ni_cambia()
    {
        _clock.UtcNow = AppointmentMother.StartsAt.AddHours(-HoursBeforeStartInsideDeadline);
        var pending = Candidate(FirstId, AppointmentStatus.Pending);

        var result = await Handle(FirstId);

        result.Value.ShouldBe(0);
        pending.Status.ShouldBe(AppointmentStatus.Pending);
        pending.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_id_cuya_cita_ya_no_existe_se_salta_y_el_resto_se_procesa()
    {
        _appointments.GetByIdForUpdateAsync(MissingId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);
        var second = Candidate(SecondId, AppointmentStatus.Pending);

        var result = await Handle(MissingId, SecondId);

        result.Value.ShouldBe(1);
        second.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Sin_candidatas_devuelve_cero_y_no_bloquea_ninguna_cita()
    {
        var result = await Handle();

        result.Value.ShouldBe(0);
        await _appointments.DidNotReceiveWithAnyArgs().GetByIdForUpdateAsync(default, default);
    }

    [Fact]
    public async Task Ejecutarlo_dos_veces_sobre_las_mismas_citas_es_idempotente()
    {
        Candidate(FirstId, AppointmentStatus.Pending);
        Candidate(SecondId, AppointmentStatus.Pending);

        var first = await Handle(FirstId, SecondId);
        var second = await Handle(FirstId, SecondId);

        first.Value.ShouldBe(2);
        second.Value.ShouldBe(0);
    }

    private Appointment Candidate(int id, AppointmentStatus status)
    {
        var appointment = AppointmentMother.InStatus(status);
        _appointments.GetByIdForUpdateAsync(id, Arg.Any<CancellationToken>()).Returns(appointment);
        return appointment;
    }

    private async Task<Result<int>> Handle(params int[] candidateIds)
    {
        _appointments.ListExpirableIdsAsync(
                Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(candidateIds);

        return await _handler.HandleAsync(new ExpirePendingAppointmentsCommand(), CancellationToken.None);
    }
}
