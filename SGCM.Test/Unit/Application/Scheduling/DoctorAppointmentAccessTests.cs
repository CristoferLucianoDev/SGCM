using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Scheduling;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class DoctorAppointmentAccessTests
{
    private const int UserId = 42;
    private const int AppointmentId = 99;
    private const int OtherDoctorId = 500;

    private static readonly Error NotDoctor = AppointmentErrors.DecisionDoctorOnly;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IAppointmentRepository _appointments = Substitute.For<IAppointmentRepository>();
    private readonly Appointment _appointment = AppointmentMother.InStatus(AppointmentStatus.Pending);
    private readonly DoctorAppointmentAccess _access;

    public DoctorAppointmentAccessTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((int?)AppointmentMother.DoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(_appointment);
        _access = new DoctorAppointmentAccess(_currentUser, _doctors, _appointments);
    }

    [Fact]
    public async Task Una_cita_propia_se_entrega_bloqueada_con_el_token()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await _access.GetOwnForUpdateAsync(AppointmentId, NotDoctor, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(_appointment);
        await _appointments.Received(1).GetByIdForUpdateAsync(AppointmentId, cancellation.Token);
    }

    [Fact]
    public async Task Sin_autenticar_devuelve_el_error_recibido_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await _access.GetOwnForUpdateAsync(AppointmentId, NotDoctor, CancellationToken.None);

        result.Error.ShouldBe(NotDoctor);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Con_otro_rol_devuelve_el_error_recibido_sin_consultar_nada()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        var result = await _access.GetOwnForUpdateAsync(AppointmentId, NotDoctor, CancellationToken.None);

        result.Error.ShouldBe(NotDoctor);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_se_rechaza_sin_buscar_la_cita()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        var result = await _access.GetOwnForUpdateAsync(AppointmentId, NotDoctor, CancellationToken.None);

        result.Error.ShouldBe(DoctorErrors.NotFound);
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Una_cita_inexistente_se_rechaza_como_no_encontrada()
    {
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns((Appointment?)null);

        var result = await _access.GetOwnForUpdateAsync(AppointmentId, NotDoctor, CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.NotFound);
    }

    [Fact]
    public async Task Una_cita_de_otro_medico_responde_igual_que_una_inexistente()
    {
        var foreign = AppointmentMother.InStatus(AppointmentStatus.Pending);
        typeof(Appointment).GetProperty(nameof(Appointment.DoctorId))!.SetValue(foreign, OtherDoctorId);
        _appointments.GetByIdForUpdateAsync(AppointmentId, Arg.Any<CancellationToken>()).Returns(foreign);

        var result = await _access.GetOwnForUpdateAsync(AppointmentId, NotDoctor, CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.NotFound);
    }

    private void AssertNothingQueried()
    {
        _doctors.ReceivedCalls().ShouldBeEmpty();
        _appointments.ReceivedCalls().ShouldBeEmpty();
    }
}
