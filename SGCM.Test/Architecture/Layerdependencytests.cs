using NetArchTest.Rules;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Web.Api.Infrastructure;
using Shouldly;
using System.Reflection;

namespace SGCM.Test.Architecture;

/// <summary>
/// Reglas de dependencia entre capas (Clean Architecture). Las capas son carpetas,
/// por eso las reglas se expresan por espacio de nombres dentro de cada ensamblado.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class LayerDependencyTests
{
    private static readonly Assembly DataAssembly = typeof(Doctor).Assembly;
    private static readonly Assembly WebAssembly = typeof(ApiControllerBase).Assembly;

    private const string Domain = "SGCM.Data.Domain";
    private const string Application = "SGCM.Data.Application";
    private const string Infrastructure = "SGCM.Data.Infrastructure";
    private const string Web = "SGCM.Web";

    [Fact]
    public void Domain_NoDependeDeOtrasCapas()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().ResideInNamespace(Domain)
            .ShouldNot().HaveDependencyOnAny(Application, Infrastructure, Web)
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Domain_NoDependeDeFrameworks()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().ResideInNamespace(Domain)
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Npgsql")
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Application_NoDependeDeInfraestructuraNiWeb()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().ResideInNamespace(Application)
            .ShouldNot().HaveDependencyOnAny(Infrastructure, Web)
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Application_NoDependeDeAspNetCoreNiNpgsql()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().ResideInNamespace(Application)
            .ShouldNot().HaveDependencyOnAny("Microsoft.AspNetCore", "Npgsql")
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Infrastructure_NoDependeDeWeb()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().ResideInNamespace(Infrastructure)
            .ShouldNot().HaveDependencyOn(Web)
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void Controladores_NoUsanInfraestructura()
    {
        var result = Types.InAssembly(WebAssembly)
            .That().ResideInNamespace($"{Web}.Controllers")
            .Or().ResideInNamespace($"{Web}.Api.Controllers")
            .ShouldNot().HaveDependencyOn(Infrastructure)
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void InterfacesDeRepositorio_EstanEnElDominio()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().AreInterfaces()
            .And().HaveNameEndingWith("Repository")
            .Should().ResideInNamespace(Domain)
            .GetResult();

        AssertSuccess(result);
    }

    [Fact]
    public void ImplementacionesDeRepositorio_EstanEnInfraestructura()
    {
        var result = Types.InAssembly(DataAssembly)
            .That().AreClasses()
            .And().HaveNameEndingWith("Repository")
            .Should().ResideInNamespace($"{Infrastructure}.Persistence.Repositories")
            .GetResult();

        AssertSuccess(result);
    }

    private static void AssertSuccess(TestResult result)
    {
        var failing = result.FailingTypeNames ?? [];
        result.IsSuccessful.ShouldBeTrue(
            $"Tipos que rompen la regla:{Environment.NewLine}{string.Join(Environment.NewLine, failing)}");
    }
}