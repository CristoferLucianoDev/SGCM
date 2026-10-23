using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Contracts.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public class SpecialtiesControllerTests
{
    private readonly ICommandHandler<CreateSpecialtyCommand, int> _handler = Substitute.For<ICommandHandler<CreateSpecialtyCommand, int>>();

    [Fact]
    public async Task Create_mapea_la_solicitud_y_devuelve_201_con_el_id()
    {
        using var cancellation = new CancellationTokenSource();
        _handler.HandleAsync(Arg.Any<CreateSpecialtyCommand>(), cancellation.Token).Returns(42);
        var controller = new SpecialtiesController();

        var result = await controller.Create(new CreateSpecialtyRequest("CARDIOLOGÍA", "Descripción válida."), _handler, cancellation.Token);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(201);
        response.Value.ShouldBeOfType<CreateSpecialtyResponse>().Id.ShouldBe(42);
        await _handler.Received(1).HandleAsync(new CreateSpecialtyCommand("CARDIOLOGÍA", "Descripción válida."), cancellation.Token);
    }

    [Theory]
    [InlineData(false, 400)]
    [InlineData(true, 409)]
    public async Task Create_devuelve_errores_mediante_ApiControllerBase(bool duplicate, int expectedStatus)
    {
        var error = duplicate ? SpecialtyErrors.NameAlreadyInUse : SpecialtyErrors.NameRequired;
        _handler.HandleAsync(Arg.Any<CreateSpecialtyCommand>(), Arg.Any<CancellationToken>()).Returns(Result<int>.Failure(error));

        var result = await new SpecialtiesController().Create(
            new CreateSpecialtyRequest("Cardiología", "Descripción válida."), _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(expectedStatus);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(expectedStatus);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }
}
