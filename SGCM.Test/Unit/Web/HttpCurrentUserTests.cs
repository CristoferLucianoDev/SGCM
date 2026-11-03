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

    [Fact]
    public void UserId_lee_el_claim_de_la_identidad_autenticada()
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.UserId, "7")], "Bearer", AppClaims.UserId, AppClaims.Role));

        user.UserId.ShouldBe(7);
    }

    [Fact]
    public void UserId_sin_contexto_lanza_excepcion()
    {
        var user = new HttpCurrentUser(new HttpContextAccessor());

        Should.Throw<InvalidOperationException>(() => user.UserId);
    }

    [Fact]
    public void UserId_de_una_identidad_no_autenticada_lanza_excepcion()
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.UserId, "7")], null, AppClaims.UserId, AppClaims.Role));

        Should.Throw<InvalidOperationException>(() => user.UserId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-es-un-id")]
    public void UserId_sin_claim_valido_lanza_excepcion(string? value)
    {
        Claim[] claims = value is null ? [] : [new Claim(AppClaims.UserId, value)];
        var user = CreateUser(new ClaimsIdentity(claims, "Bearer", AppClaims.UserId, AppClaims.Role));

        Should.Throw<InvalidOperationException>(() => user.UserId);
    }

    [Fact]
    public void Identidad_de_sesion_lee_ambos_ids_de_la_misma_identidad_autenticada()
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.UserId, "7"), new Claim(AppClaims.SessionId, "42")], "Bearer"));
        user.TryGetSessionIdentity(out var userId, out var sessionId).ShouldBeTrue();
        userId.ShouldBe(7);
        sessionId.ShouldBe(42);
    }

    [Theory]
    [InlineData(null, "42")]
    [InlineData("7", null)]
    [InlineData("x", "42")]
    [InlineData("7", "x")]
    [InlineData("0", "42")]
    [InlineData("-1", "42")]
    [InlineData("7", "0")]
    [InlineData("7", "-1")]
    [InlineData("2147483648", "42")]
    [InlineData("7", "2147483648")]
    public void Identidad_de_sesion_rechaza_ids_ausentes_o_invalidos(string? sub, string? sid)
    {
        var claims = new List<Claim>();
        if (sub is not null) claims.Add(new Claim(AppClaims.UserId, sub));
        if (sid is not null) claims.Add(new Claim(AppClaims.SessionId, sid));
        var user = CreateUser(new ClaimsIdentity(claims, "Bearer"));
        AssertInvalidSessionIdentity(user);
    }

    [Theory]
    [InlineData(AppClaims.UserId)]
    [InlineData(AppClaims.SessionId)]
    public void Identidad_de_sesion_rechaza_claims_duplicados_incluso_iguales(string type)
    {
        var user = CreateUser(new ClaimsIdentity(
            [new Claim(AppClaims.UserId, "7"), new Claim(AppClaims.SessionId, "42"),
             new Claim(type, type == AppClaims.UserId ? "7" : "42")], "Bearer"));
        AssertInvalidSessionIdentity(user);
    }

    [Fact]
    public void Identidad_de_sesion_no_mezcla_identidades_y_rechaza_ambiguedad()
    {
        AssertInvalidSessionIdentity(CreateUser(
            new ClaimsIdentity([new Claim(AppClaims.UserId, "7")], "Bearer"),
            new ClaimsIdentity([new Claim(AppClaims.SessionId, "42")], "Bearer")));
        AssertInvalidSessionIdentity(CreateUser(
            new ClaimsIdentity([new Claim(AppClaims.UserId, "7"), new Claim(AppClaims.SessionId, "42")], "Bearer"),
            new ClaimsIdentity([new Claim(AppClaims.UserId, "8"), new Claim(AppClaims.SessionId, "43")], "Other")));
    }

    [Fact]
    public void Identidad_de_sesion_ignora_claims_no_autenticados_y_rechaza_anonimos()
    {
        Claim[] claims = [new Claim(AppClaims.UserId, "7"), new Claim(AppClaims.SessionId, "42")];
        AssertInvalidSessionIdentity(new HttpCurrentUser(new HttpContextAccessor()));
        AssertInvalidSessionIdentity(CreateUser(new ClaimsIdentity(claims)));
        AssertInvalidSessionIdentity(CreateUser(new ClaimsIdentity([], "Bearer"), new ClaimsIdentity(claims)));
        CreateUser(new ClaimsIdentity(claims, "Bearer"), new ClaimsIdentity(claims))
            .TryGetSessionIdentity(out _, out _).ShouldBeTrue();
    }

    private static void AssertInvalidSessionIdentity(HttpCurrentUser user)
    {
        user.TryGetSessionIdentity(out var userId, out var sessionId).ShouldBeFalse();
        userId.ShouldBe(0);
        sessionId.ShouldBe(0);
    }

    private static HttpCurrentUser CreateUser(params ClaimsIdentity[] identities) => new(new HttpContextAccessor
    {
        HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identities) }
    });
}
