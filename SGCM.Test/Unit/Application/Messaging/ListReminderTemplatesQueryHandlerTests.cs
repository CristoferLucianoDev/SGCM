using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.Messaging.Queries.ListReminderTemplates;
using SGCM.Data.Application.Messaging.ReminderTemplates;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Data.Domain.People.Doctors;
using Shouldly;

namespace SGCM.Test.Unit.Application.Messaging;

/// <summary>Pruebas de <see cref="ListReminderTemplatesQueryHandler"/>.</summary>
public sealed class ListReminderTemplatesQueryHandlerTests
{
    private const int UserId = 42;
    private const int DoctorId = 7;

    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IActiveDoctorReader _doctors = Substitute.For<IActiveDoctorReader>();
    private readonly IReminderTemplateListReader _reader = Substitute.For<IReminderTemplateListReader>();

    public ListReminderTemplatesQueryHandlerTests()
    {
        _currentUser.IsAuthenticated.Returns(true);
        _currentUser.IsInRole(UserRole.Doctor).Returns(true);
        _currentUser.UserId.Returns(UserId);
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)DoctorId);
    }

    [Fact]
    public async Task Devuelve_la_pagina_del_lector_para_el_medico_autenticado()
    {
        var page = new PagedResult<ReminderTemplateDto>(
            [new ReminderTemplateDto(1, "Recordatorio de cita", "Asunto", "Cuerpo", true, true)], 2, 10, 11);
        _reader.ListAsync(DoctorId, 2, 10, Arg.Any<CancellationToken>()).Returns(page);

        (await Handle(new ListReminderTemplatesQuery(2, 10))).Value.ShouldBeSameAs(page);
    }

    [Theory]
    [InlineData(0, 20, "ReminderTemplate.InvalidPageNumber")]
    [InlineData(1, 0, "ReminderTemplate.InvalidPageSize")]
    [InlineData(1, 101, "ReminderTemplate.InvalidPageSize")]
    [InlineData(int.MaxValue, 100, "ReminderTemplate.PageOffsetTooLarge")]
    public async Task Una_paginacion_invalida_se_rechaza_sin_leer(int pageNumber, int pageSize, string code)
    {
        var result = await Handle(new ListReminderTemplatesQuery(pageNumber, pageSize));

        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_usuario_que_no_es_medico_se_rechaza()
    {
        _currentUser.IsInRole(UserRole.Doctor).Returns(false);

        (await Handle(new ListReminderTemplatesQuery())).Error.ShouldBe(ReminderTemplateErrors.DoctorOnly);
        _reader.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Un_medico_sin_perfil_activo_responde_medico_no_encontrado()
    {
        _doctors.FindActiveDoctorIdByUserIdAsync(UserId, Arg.Any<CancellationToken>()).Returns((int?)null);

        (await Handle(new ListReminderTemplatesQuery())).Error.ShouldBe(DoctorErrors.NotFound);
    }

    private Task<Result<PagedResult<ReminderTemplateDto>>> Handle(ListReminderTemplatesQuery query) =>
        new ListReminderTemplatesQueryHandler(
                new DoctorReminderTemplateAccess(_currentUser, _doctors, Substitute.For<IReminderTemplateRepository>()),
                _reader,
                new ListReminderTemplatesQueryValidator())
            .HandleAsync(query, CancellationToken.None);
}
