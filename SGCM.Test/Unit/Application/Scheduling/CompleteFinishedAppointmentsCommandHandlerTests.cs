using NSubstitute;
using SGCM.Data.Application.Scheduling.Commands.CompleteFinishedAppointments;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class CompleteFinishedAppointmentsCommandHandlerTests
{
    private const int FirstId = 11;
    private const int SecondId = 12;
    private const int MissingId = 13;
    private const int HoursIntoTheAppointment = 1;
    private const int ExtraHours = 1;
    private const int ExpectedBatchSize = 100;

    private static readonly DateTimeOffset StartsAt = AppointmentMother.StartsAt;
    private static readonly DateTimeOffset EndsAt = StartsAt.AddHours(AppointmentMother.DurationHours);

    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly FakeDateTimeProvider _clock = new();
    private readonly CompleteFinishedAppointmentsCommandHandler _handler;

    public CompleteFinishedAppointmentsCommandHandlerTests()
    {
        _clock.UtcNow = EndsAt;
        _handler = new CompleteFinishedAppointmentsCommandHandler(_appointments, _clock);
    }

    [Fact]
    public async Task Completa_las_terminadas_las_cuenta_y_emite_el_evento_de_cada_una()
    {
        var first = Candidate(FirstId, AppointmentStatus.Confirmed);
        var second = Candidate(SecondId, AppointmentStatus.Confirmed);

        var result = await Handle(FirstId, SecondId);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(2);
        foreach (var appointment in new[] { first, second })
        {
            appointment.Status.ShouldBe(AppointmentStatus.Completed);
            appointment.EndsAt.ShouldBe(EndsAt);
            appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCompletedEvent>()
                .Appointment.ShouldBeSameAs(appointment);
        }
    }

    [Fact]
    public async Task Pasa_al_repositorio_el_instante_del_reloj_el_tamano_del_lote_y_el_token()
    {
        using var source = new CancellationTokenSource();
        _appointments.ListFinishedIdsAsync(default, default, default).ReturnsForAnyArgs(Array.Empty<int>());

        await _handler.HandleAsync(new CompleteFinishedAppointmentsCommand(), source.Token);

        await _appointments.Received(1).ListFinishedIdsAsync(_clock.UtcNow, ExpectedBatchSize, source.Token);
    }

    [Theory]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.Cancelled)]
    public async Task Una_candidata_que_ya_esta_cerrada_no_se_cuenta_ni_cambia(AppointmentStatus status)
    {
        var closed = Candidate(FirstId, status);

        var result = await Handle(FirstId);

        result.Value.ShouldBe(0);
        closed.Status.ShouldBe(status);
        closed.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_que_el_medico_extendio_no_se_completa_porque_su_fin_nuevo_es_futuro()
    {
        var extended = Candidate(FirstId, AppointmentStatus.Confirmed);
        TimeRange[] blocks = [TimeRange.Create(StartsAt, EndsAt.AddHours(ExtraHours)).Value];
        extended.Extend(ExtraHours, StartsAt.AddHours(HoursIntoTheAppointment), blocks, []).IsSuccess.ShouldBeTrue();
        extended.ClearDomainEvents();

        var result = await Handle(FirstId);

        result.Value.ShouldBe(0);
        extended.Status.ShouldBe(AppointmentStatus.Confirmed);
        extended.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_id_cuya_cita_ya_no_existe_se_salta_y_el_resto_se_procesa()
    {
        _appointments.GetByIdForUpdateAsync(MissingId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);
        var second = Candidate(SecondId, AppointmentStatus.Confirmed);

        var result = await Handle(MissingId, SecondId);

        result.Value.ShouldBe(1);
        second.Status.ShouldBe(AppointmentStatus.Completed);
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
        Candidate(FirstId, AppointmentStatus.Confirmed);
        Candidate(SecondId, AppointmentStatus.Confirmed);

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
        _appointments.ListFinishedIdsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(candidateIds);

        return await _handler.HandleAsync(new CompleteFinishedAppointmentsCommand(), CancellationToken.None);
    }
}
