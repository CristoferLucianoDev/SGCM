using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using NSubstitute;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;
using SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Web.Api.Controllers.People;
using SGCM.Web.Api.Infrastructure;
using Shouldly;

namespace SGCM.Test.Unit.Web;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class DoctorsControllerTests
{
    private readonly DoctorsController _controller = new();
    private readonly ICommandHandler<AssociateDoctorSpecialtyCommand, Unit> _associate =
        Substitute.For<ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>>();
    private readonly ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit> _disassociate =
        Substitute.For<ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>>();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operacion_pasa_solo_SpecialtyId_y_token_al_handler_correcto_y_devuelve_204(bool associate)
    {
        using var cancellation = new CancellationTokenSource();
        _associate.HandleAsync(new(22), cancellation.Token).Returns(Unit.Value);
        _disassociate.HandleAsync(new(22), cancellation.Token).Returns(Unit.Value);

        var result = await Handle(associate, 22, cancellation.Token);

        result.ShouldBeOfType<NoContentResult>().StatusCode.ShouldBe(204);
        if (associate)
        {
            await _associate.Received(1).HandleAsync(new(22), cancellation.Token);
            _disassociate.ReceivedCalls().ShouldBeEmpty();
        }
        else
        {
            await _disassociate.Received(1).HandleAsync(new(22), cancellation.Token);
            _associate.ReceivedCalls().ShouldBeEmpty();
        }
    }

    public static IEnumerable<object[]> Errors()
    {
        yield return [true, SpecialtyErrors.InvalidId, 400];
        yield return [true, DoctorErrors.NotFound, 404];
        yield return [true, SpecialtyErrors.NotFound, 404];
        yield return [true, SpecialtyErrors.Inactive, 409];
        yield return [true, DoctorErrors.ProfileIncomplete, 409];
        yield return [true, DoctorErrors.SpecialtyAlreadyAssociated, 409];
        yield return [false, SpecialtyErrors.InvalidId, 400];
        yield return [false, DoctorErrors.NotFound, 404];
        yield return [false, DoctorErrors.ProfileIncomplete, 409];
        yield return [false, DoctorErrors.SpecialtyNotAssociated, 409];
        yield return [false, DoctorErrors.CannotRemoveLastSpecialty, 409];
    }

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Operacion_utiliza_ErrorResult_con_estado_codigo_y_descripcion(bool associate, Error error, int status)
    {
        _associate.HandleAsync(new(22), CancellationToken.None).Returns(Result<Unit>.Failure(error));
        _disassociate.HandleAsync(new(22), CancellationToken.None).Returns(Result<Unit>.Failure(error));

        var result = await Handle(associate, 22, CancellationToken.None);

        var response = result.ShouldBeOfType<ObjectResult>();
        response.StatusCode.ShouldBe(status);
        var problem = response.Value.ShouldBeOfType<ProblemDetails>();
        problem.Status.ShouldBe(status);
        problem.Title.ShouldBe(error.Description);
        problem.Extensions["code"].ShouldBe(error.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Operacion_propaga_cancelacion_del_handler(bool associate)
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _associate.HandleAsync(new(22), cancellation.Token).Returns(Task.FromCanceled<Result<Unit>>(cancellation.Token));
        _disassociate.HandleAsync(new(22), cancellation.Token).Returns(Task.FromCanceled<Result<Unit>>(cancellation.Token));

        var error = await Should.ThrowAsync<OperationCanceledException>(() => Handle(associate, 22, cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Contrato_solo_admite_SpecialtyId_de_ruta_sin_cuerpo_y_exige_rol_Doctor(bool associate)
    {
        var type = typeof(DoctorsController);
        type.GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/doctors/me/specialties");
        type.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        type.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var method = Action(associate);
        var verb = method.GetCustomAttributes<HttpMethodAttribute>().ShouldHaveSingleItem();
        verb.Template.ShouldBe("{specialtyId:int}");
        verb.HttpMethods.ShouldBe(new[] { associate ? "POST" : "DELETE" });
        method.GetCustomAttributes<AuthorizeAttribute>().ShouldHaveSingleItem().Roles.ShouldBe(nameof(UserRole.Doctor));
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
        method.GetCustomAttributes<AllowPendingStepAttribute>().ShouldBeEmpty();
        var parameters = method.GetParameters();
        parameters.Length.ShouldBe(3);
        var route = parameters.Single(value => value.Name == "specialtyId");
        route.ParameterType.ShouldBe(typeof(int));
        route.GetCustomAttribute<FromRouteAttribute>().ShouldNotBeNull();
        var handlerType = associate ? typeof(ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>)
            : typeof(ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>);
        parameters.Single(value => value.ParameterType == handlerType)
            .GetCustomAttribute<FromServicesAttribute>().ShouldNotBeNull();
        parameters.Single(value => value.ParameterType == typeof(CancellationToken)).ShouldNotBeNull();
        parameters.ShouldAllBe(value => value.GetCustomAttribute<FromBodyAttribute>() == null
            && value.GetCustomAttribute<FromQueryAttribute>() == null);
        parameters.ShouldAllBe(value => value.Name != "doctorId" && value.Name != "userId");
        var command = associate ? typeof(AssociateDoctorSpecialtyCommand) : typeof(DisassociateDoctorSpecialtyCommand);
        command.GetProperties().Select(value => value.Name).ShouldBe(new[] { "SpecialtyId" });
        var responses = method.GetCustomAttributes<ProducesResponseTypeAttribute>().ToArray();
        responses.Select(value => value.StatusCode).ShouldBe(new[] { 204, 400, 401, 403, 404, 409 }, ignoreOrder: true);
        responses.Where(value => value.StatusCode is 400 or 404 or 409)
            .ShouldAllBe(value => value.Type == typeof(ProblemDetails));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Contrato_conserva_el_bloqueo_de_PendingStepFilter_para_doctor_profile(bool associate)
    {
        var metadata = typeof(DoctorsController).GetCustomAttributes().Concat(Action(associate).GetCustomAttributes())
            .Cast<object>().ToList();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(AppClaims.UserId, "42"),
                new Claim(AppClaims.Role, nameof(UserRole.Doctor)),
                new Claim(AppClaims.PendingStep, PendingStepClaimValues.DoctorProfile)
            ], "Bearer", AppClaims.UserId, AppClaims.Role))
        };
        var action = new ActionDescriptor { EndpointMetadata = metadata };
        var authorization = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), action), []);

        new PendingStepFilter().OnAuthorization(authorization);

        authorization.Result.ShouldBeOfType<ForbidResult>();
    }

    private Task<IActionResult> Handle(bool associate, int id, CancellationToken token) => associate
        ? _controller.Associate(id, _associate, token)
        : _controller.Disassociate(id, _disassociate, token);

    private static MethodInfo Action(bool associate) => typeof(DoctorsController)
        .GetMethod(associate ? nameof(DoctorsController.Associate) : nameof(DoctorsController.Disassociate))!;
}
