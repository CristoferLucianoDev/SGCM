using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Web.Api.Infrastructure;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class HttpCurrentUserTests
{
    [Fact]
    public void Sin_contexto_no_hay_usuario_autenticado_ni_rol()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor());

        user.IsAuthenticated.ShouldBeFalse();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Un_claim_sin_identidad_autenticada_no_concede_privilegios(string? authenticationType)
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.Role, nameof(UserRole.Administrator))], authenticationType, AppClaims.UserId, AppClaims.Role));

        user.IsAuthenticated.ShouldBeFalse();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    [Theory]
    [InlineData(UserRole.Patient)]
    [InlineData(UserRole.Doctor)]
    [InlineData(UserRole.Administrator)]
    public void La_identidad_autenticada_usa_el_tipo_de_claim_de_rol_configurado(UserRole role)
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.Role, role.ToString())], "Bearer", AppClaims.UserId, AppClaims.Role));

        user.IsAuthenticated.ShouldBeTrue();
        foreach (var candidate in Enum.GetValues<UserRole>())
        {
            user.IsInRole(candidate).ShouldBe(candidate == role);
        }
    }

    [Fact]
    public void Una_identidad_autenticada_sin_rol_no_es_administrador()
    {
        var user = CreateUser(new ClaimsIdentity([], "Bearer", AppClaims.UserId, AppClaims.Role));

        user.IsAuthenticated.ShouldBeTrue();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    [Theory]
    [InlineData("administrator")]
    [InlineData("3")]
    [InlineData("Unknown")]
    [InlineData("")]
    public void Un_valor_distinto_del_nombre_exacto_del_rol_no_concede_privilegios(string role)
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.Role, role)], "Bearer", AppClaims.UserId, AppClaims.Role));

        user.IsAuthenticated.ShouldBeTrue();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    [Fact]
    public void Un_claim_de_otro_tipo_no_sustituye_el_claim_de_rol_configurado()
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, nameof(UserRole.Administrator))], "Bearer", AppClaims.UserId, AppClaims.Role));

        user.IsAuthenticated.ShouldBeTrue();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    [Fact]
    public void No_combina_la_autenticacion_de_una_identidad_con_el_rol_de_otra_no_autenticada()
    {
        var user = CreateUser(
            new ClaimsIdentity([], "Bearer", AppClaims.UserId, AppClaims.Role),
            new ClaimsIdentity([new Claim(AppClaims.Role, nameof(UserRole.Administrator))], null, AppClaims.UserId, AppClaims.Role));

        user.IsAuthenticated.ShouldBeTrue();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    [Fact]
    public void Un_contexto_sin_identidad_autenticada_es_anonimo()
    {
        var user = CreateUser(new ClaimsIdentity());

        user.IsAuthenticated.ShouldBeFalse();
        user.IsInRole(UserRole.Administrator).ShouldBeFalse();
    }

    private static HttpCurrentUser CreateUser(params ClaimsIdentity[] identities) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identities) }
    });
}
