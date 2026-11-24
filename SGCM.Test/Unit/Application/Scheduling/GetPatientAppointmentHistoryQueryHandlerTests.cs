using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Scheduling.Queries.GetPatientAppointmentHistory;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.Scheduling;

public sealed class GetPatientAppointmentHistoryQueryHandlerTests
{
    private const int UserId = 42;
    private const int PatientId = 11;
    private const int PageNumber = 2;
    private const int PageSize = 50;

    private static readonly DateOnly From = new(2027, 1, 1);
    private static readonly DateOnly To = new(2027, 3, 1);

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IPatientRepository _patients = Substitute.For<IPatientRepository>();
    private readonly IPatientAppointmentHistoryReader _reader = Substitute.For<IPatientAppointmentHistoryReader>();

    public GetPatientAppointmentHistoryQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Patient).Returns(true);
        _currentUser.UserId.Returns(UserId);
        var patient = PatientMother.WithRequiredData(UserId);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(patient, PatientId);
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns(patient);
    }

    [Fact]
    public async Task Devuelve_la_pagina_del_lector_con_el_paciente_autenticado_los_filtros_y_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var page = new PagedResult<PatientAppointmentDto>([], PageNumber, PageSize, 0);
        _reader.ListAsync(
                PatientId, AppointmentStatus.Cancelled, From, To, PageNumber, PageSize, cancellation.Token)
            .Returns(page);

        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentHistoryQuery(AppointmentStatus.Cancelled, From, To, PageNumber, PageSize),
            cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBeSameAs(page);
        await _reader.Received(1).ListAsync(
            PatientId, AppointmentStatus.Cancelled, From, To, PageNumber, PageSize, cancellation.Token);
    }

    [Fact]
    public async Task Una_consulta_invalida_devuelve_el_error_del_validador_sin_consultar_nada()
    {
        var result = await CreateHandler().HandleAsync(
            new GetPatientAppointmentHistoryQuery(PageNumber: 0), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("Appointment.InvalidPageNumber");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_autenticar_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsAuthenticated.Returns(false);

        var result = await CreateHandler().HandleAsync(new GetPatientAppointmentHistoryQuery(), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.HistoryPatientOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Con_otro_rol_se_rechaza_sin_consultar_nada()
    {
        _currentUser.IsInRole(UserRole.Patient).Returns(false);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);

        var result = await CreateHandler().HandleAsync(new GetPatientAppointmentHistoryQuery(), CancellationToken.None);

        result.Error.ShouldBe(AppointmentErrors.HistoryPatientOnly);
        AssertNothingQueried();
    }

    [Fact]
    public async Task Sin_perfil_de_paciente_se_rechaza_sin_llamar_al_lector()
    {
        _patients.GetByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((Patient?)null);

        var result = await CreateHandler().HandleAsync(new GetPatientAppointmentHistoryQuery(), CancellationToken.None);

        result.Error.ShouldBe(PatientErrors.ProfileNotFound);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private void AssertNothingQueried()
    {
        _patients.ReceivedCalls().ShouldBeEmpty();
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    private GetPatientAppointmentHistoryQueryHandler CreateHandler() =>
        new(_currentUser, _patients, _reader, new GetPatientAppointmentHistoryQueryValidator());
}
