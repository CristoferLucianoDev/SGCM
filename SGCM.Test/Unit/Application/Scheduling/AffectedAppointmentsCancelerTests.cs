using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Data.Domain.Scheduling.Appointments.Events;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class AffectedAppointmentsCancelerTests
{
    private const int DoctorId = AppointmentMother.DoctorId;
    private const string Reason = "Congreso médico";
    private static readonly ShortNoticeCancellation ShortNotice = new(6, 30);
    private static readonly TimeRange Removed =
        TimeRange.Create(AppointmentMother.StartsAt, AppointmentMother.StartsAt.AddHours(6)).Value;

    private readonly IAffectedAppointmentsPolicy _policy = Substitute.For<IAffectedAppointmentsPolicy>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly FakeDateTimeProvider _clock = new() { UtcNow = AppointmentMother.StartsAt.AddDays(-3) };
    private readonly Appointment _first = Saved(11, 0);
    private readonly Appointment _second = Saved(12, 2);

    public AffectedAppointmentsCancelerTests()
    {
        _appointments.GetByIdForUpdateAsync(11, Arg.Any<CancellationToken>()).Returns(_first);
        _appointments.GetByIdForUpdateAsync(12, Arg.Any<CancellationToken>()).Returns(_second);
        GivenAffected(isShortNotice: false, _first, _second);
    }

    [Fact]
    public async Task Cancela_cada_cita_afectada_como_cancelacion_del_medico_con_el_motivo_y_guarda()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await CreateCanceler().CancelAsync(DoctorId, [Removed], Reason, cancellation.Token);

        result.Value.ShouldBe(2);
        foreach (var appointment in new[] { _first, _second })
        {
            appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
            appointment.StatusReason.ShouldBe(Reason);
            appointment.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<AppointmentCancelledEvent>()
                .CancelledBy.ShouldBe(AppointmentCanceller.Doctor);
        }
        await _policy.Received(1).EvaluateAsync(
            DoctorId, Arg.Is<IReadOnlyCollection<TimeRange>>(ranges => ranges.Single() == Removed), cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Fact]
    public async Task Lee_cada_cita_bloqueada_antes_de_cancelarla()
    {
        await CreateCanceler().CancelAsync(DoctorId, [Removed], Reason, CancellationToken.None);

        await _appointments.Received(1).GetByIdForUpdateAsync(11, CancellationToken.None);
        await _appointments.Received(1).GetByIdForUpdateAsync(12, CancellationToken.None);
        await _appointments.DidNotReceive().GetByIdAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Sin_citas_afectadas_no_cancela_ni_guarda_y_no_exige_motivo()
    {
        GivenAffected(isShortNotice: false);

        (await CreateCanceler().CancelAsync(DoctorId, [Removed], null, CancellationToken.None)).Value.ShouldBe(0);

        _appointments.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_una_cita_no_acepta_el_motivo_devuelve_su_error_sin_guardar()
    {
        var result = await CreateCanceler().CancelAsync(DoctorId, [Removed], null, CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.StatusReasonRequired);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Con_aviso_corto_usa_la_regla_de_la_politica_para_las_palabras_minimas()
    {
        _clock.UtcNow = AppointmentMother.StartsAt.AddHours(-1);
        GivenAffected(isShortNotice: true, _first);

        var result = await CreateCanceler().CancelAsync(DoctorId, [Removed], Reason, CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.ShortNoticeReasonTooShort(30));
    }

    [Fact]
    public async Task Una_cita_que_ya_no_existe_al_bloquearla_se_omite()
    {
        _appointments.GetByIdForUpdateAsync(12, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        var result = await CreateCanceler().CancelAsync(DoctorId, [Removed], Reason, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        _first.Status.ShouldBe(AppointmentStatus.Cancelled);
    }

    private void GivenAffected(bool isShortNotice, params Appointment[] appointments) =>
        _policy.EvaluateAsync(Arg.Any<int>(), Arg.Any<IReadOnlyCollection<TimeRange>>(), Arg.Any<CancellationToken>())
            .Returns(new AffectedAppointments(
                appointments.Select(appointment => new BookedInterval(
                    appointment.Id, TimeRange.Create(appointment.StartsAt, appointment.EndsAt).Value)).ToList(),
                isShortNotice,
                ShortNotice));

    private AffectedAppointmentsCanceler CreateCanceler() => new(
        _policy,
        _appointments,
        _unitOfWork,
        _clock,
        Options.Create(new SchedulingOptions
        {
            PatientCancellationDeadlineHours = 24,
            DoctorLateCancellationThresholdHours = 6,
            DoctorLateCancellationMinimumWords = 30
        }));

    private static Appointment Saved(int id, int hoursAfterStart)
    {
        var appointment = AppointmentMother.Requested(AppointmentMother.StartsAt.AddHours(hoursAfterStart));
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(appointment, id);
        appointment.ClearDomainEvents();
        return appointment;
    }
}
