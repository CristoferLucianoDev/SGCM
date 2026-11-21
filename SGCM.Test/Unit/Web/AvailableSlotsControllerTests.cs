using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Scheduling.Queries.GetAvailableSlots;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Web.Api.Controllers.Scheduling;
using SGCM.Web.Api.Infrastructure;
using SGCM.Web.Contracts.Scheduling;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class AvailableSlotsControllerTests
{
    private const int DoctorId = 7;

    private static readonly DateTimeOffset First = new(2027, 1, 10, 8, 0, 0, TimeSpan.Zero);

    private readonly IQueryHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>> _handler =
        Substitute.For<IQueryHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>>>();

    [Fact]
    public async Task ListByDoctor_pasa_el_id_y_el_token_al_handler_y_responde_200_con_los_horarios()
    {
        using var cancellation = new CancellationTokenSource();
        GivenSlots(new AvailableSlotDto(First, First.AddHours(2)), new AvailableSlotDto(First.AddHours(1), First.AddHours(3)));

        var result = await new AvailableSlotsController().ListByDoctor(DoctorId, _handler, cancellation.Token);

        // Items es una lista: el record la compara por referencia, así que se comparan sus elementos.
        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AvailableSlotsResponse>();
        response.Items.ShouldBe(new[]
        {
            new AvailableSlotResponse(First, First.AddHours(2)),
            new AvailableSlotResponse(First.AddHours(1), First.AddHours(3))
        });
        await _handler.Received(1).HandleAsync(new GetAvailableSlotsQuery(DoctorId), cancellation.Token);
    }

    [Fact]
    public async Task ListByDoctor_sin_horarios_responde_200_con_una_lista_vacia()
    {
        GivenSlots();

        var result = await new AvailableSlotsController().ListByDoctor(DoctorId, _handler, CancellationToken.None);

        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AvailableSlotsResponse>();
        response.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ListOwn_llama_con_un_DoctorId_nulo_y_responde_200_con_los_horarios()
    {
        using var cancellation = new CancellationTokenSource();
        GivenSlots(new AvailableSlotDto(First, First.AddHours(2)));

        var result = await new AvailableSlotsController().ListOwn(_handler, cancellation.Token);

        var response = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AvailableSlotsResponse>();
        response.Items.ShouldBe(new[] { new AvailableSlotResponse(First, First.AddHours(2)) });
        await _handler.Received(1).HandleAsync(new GetAvailableSlotsQuery(), cancellation.Token);
    }

    [Fact]
    public async Task ListOwn_sin_horarios_responde_200_con_una_lista_vacia()
    {
        GivenSlots();

        var result = await new AvailableSlotsController().ListOwn(_handler, CancellationToken.None);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AvailableSlotsResponse>().Items.ShouldBeEmpty();
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [AvailableSlotsErrors.DoctorOrSpecialtyRequired, 400];
        yield return [Error.Validation("AvailableSlots.InvalidDoctorId", "El identificador del médico debe ser mayor que cero."), 400];
        yield return [DoctorErrors.NotFound, 404];
        yield return [AvailableSlotsErrors.RoleNotAllowed, 403];
        yield return [AvailableSlotsErrors.OtherDoctorSchedule, 403];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task ListByDoctor_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        GivenFailure(error);

        var result = await new AvailableSlotsController().ListByDoctor(DoctorId, _handler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task ListOwn_utiliza_ErrorResult_con_estado_codigo_y_descripcion(Error error, int status)
    {
        GivenFailure(error);

        var result = await new AvailableSlotsController().ListOwn(_handler, CancellationToken.None);

        AssertProblem(result, error, status);
    }

    [Fact]
    public void Contrato_de_rutas_roles_y_respuestas_de_las_dos_acciones()
    {
        var type = typeof(AvailableSlotsController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/doctors");
        type.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        type.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();

        var byDoctor = type.GetMethod(nameof(AvailableSlotsController.ListByDoctor))!;
        AssertAction(byDoctor, "{doctorId:int}/available-slots", UserRole.Patient);
        AssertResponses(byDoctor, 200, 400, 401, 403, 404);
        byDoctor.GetCustomAttributes<ProducesResponseTypeAttribute>().Single(value => value.StatusCode == 400)
            .Type.ShouldBe(typeof(ProblemDetails));

        var own = type.GetMethod(nameof(AvailableSlotsController.ListOwn))!;
        AssertAction(own, "me/available-slots", UserRole.Doctor);
        AssertResponses(own, 200, 401, 403, 404);
        // El médico sale de la sesión: la acción solo recibe el handler y el token, sin ids de médico ni de especialidad.
        own.GetParameters().Select(parameter => parameter.ParameterType)
            .ShouldBe(new[] { typeof(IQueryHandler<GetAvailableSlotsQuery, IReadOnlyList<AvailableSlotDto>>), typeof(CancellationToken) });
    }

    [Fact]
    public void Las_dos_acciones_son_las_unicas_publicas_del_controlador()
    {
        typeof(AvailableSlotsController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Select(method => method.Name)
            .ShouldBe(new[] { nameof(AvailableSlotsController.ListByDoctor), nameof(AvailableSlotsController.ListOwn) },
                ignoreOrder: true);
    }

    private static void AssertAction(MethodInfo method, string template, UserRole role)
    {
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.HttpMethods.ShouldBe(new[] { "GET" });
        verb.Template.ShouldBe(template);
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(role.ToString());
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
    }

    private static void AssertResponses(MethodInfo method, params int[] statuses)
    {
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(statuses, ignoreOrder: true);
        responses.Single(value => value.StatusCode == 200).Type.ShouldBe(typeof(AvailableSlotsResponse));
        responses.Single(value => value.StatusCode == 404).Type.ShouldBe(typeof(ProblemDetails));
    }

    private static void AssertProblem(IActionResult result, Error error, int status)
    {
        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(status);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    private void GivenSlots(params AvailableSlotDto[] slots) =>
        _handler.HandleAsync(Arg.Any<GetAvailableSlotsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<AvailableSlotDto>>.Success(slots));

    private void GivenFailure(Error error) =>
        _handler.HandleAsync(Arg.Any<GetAvailableSlotsQuery>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<AvailableSlotDto>>.Failure(error));
}
