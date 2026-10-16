using System.Buffers.Binary;
using Microsoft.Extensions.Options;
using Shouldly;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Infrastructure.Security;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;
using PasswordHasherOptions = Microsoft.AspNetCore.Identity.PasswordHasherOptions;
using PasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace SGCM.Test.Unit.Infrastructure.Security;

public sealed class PasswordHasherTests
{
    private const int LowIterationCount = 1000;

    // En el formato V3 de Identity, el hash decodificado de Base64 empieza con este marcador y
    // guarda las iteraciones como entero big-endian a partir del byte 5.
    private const byte FormatMarkerV3 = 0x01;
    private const int IterationCountOffset = 5;

    private static readonly Password ValidPassword = Password.Create("Secreta123").Value;

    // Con pocas iteraciones para que la clase no pague el costo de producción; solo las pruebas
    // que comprueban la configuración por defecto crean su propio hasher.
    private static readonly PasswordHasherOptions LowCostOptions = new() { IterationCount = LowIterationCount };

    private readonly PasswordHasher _hasher = CreateHasher(LowCostOptions);

    [Fact]
    public void Hash_DoesNotContainPlainPassword()
    {
        var hash = _hasher.Hash(ValidPassword);

        hash.ShouldNotContain(ValidPassword.Value);
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashes()
    {
        var first = _hasher.Hash(ValidPassword);
        var second = _hasher.Hash(ValidPassword);

        first.ShouldNotBe(second);
    }

    [Fact]
    public void Hash_CanBeVerifiedByIdentityHasher()
    {
        var hash = _hasher.Hash(ValidPassword);

        // Con las mismas iteraciones que el hash: con más, Identity respondería SuccessRehashNeeded.
        var result = new IdentityPasswordHasher(Options.Create(LowCostOptions))
            .VerifyHashedPassword(new object(), hash, ValidPassword.Value);

        result.ShouldBe(PasswordVerificationResult.Success);
    }

    [Fact]
    public void Verify_con_la_contrasena_correcta_devuelve_true()
    {
        var hash = _hasher.Hash(ValidPassword);

        _hasher.Verify(hash, ValidPassword.Value).ShouldBeTrue();
    }

    [Fact]
    public void Verify_con_otra_contrasena_devuelve_false()
    {
        var hash = _hasher.Hash(ValidPassword);

        _hasher.Verify(hash, "OtraClave456").ShouldBeFalse();
    }

    // Un hash vacío se decodifica como cero bytes; uno que no es Base64 hace lanzar a Identity.
    [Theory]
    [InlineData("")]
    [InlineData("no-es-un-hash")]
    public void Verify_con_un_hash_mal_formado_devuelve_false(string malformedHash)
    {
        _hasher.Verify(malformedHash, ValidPassword.Value).ShouldBeFalse();
    }

    [Fact]
    public void Verify_sin_hash_devuelve_false()
    {
        _hasher.Verify(null, ValidPassword.Value).ShouldBeFalse();
    }

    [Fact]
    public void Verify_un_hash_con_pocas_iteraciones_se_verifica_con_la_configuracion_por_defecto()
    {
        var hash = _hasher.Hash(ValidPassword);
        var defaultHasher = CreateHasher(new PasswordHasherOptions());

        defaultHasher.Verify(hash, ValidPassword.Value).ShouldBeTrue();
    }

    [Fact]
    public void Verify_un_hash_con_la_configuracion_por_defecto_se_verifica_con_pocas_iteraciones()
    {
        var hash = CreateHasher(new PasswordHasherOptions()).Hash(ValidPassword);

        _hasher.Verify(hash, ValidPassword.Value).ShouldBeTrue();
    }

    [Fact]
    public void Hash_con_la_configuracion_por_defecto_mantiene_el_formato_V3()
    {
        var defaultHasher = CreateHasher(new PasswordHasherOptions());

        var decoded = Convert.FromBase64String(defaultHasher.Hash(ValidPassword));

        decoded[0].ShouldBe(FormatMarkerV3);
        BinaryPrimitives.ReadInt32BigEndian(decoded.AsSpan(IterationCountOffset))
            .ShouldBe(new PasswordHasherOptions().IterationCount);
    }

    private static PasswordHasher CreateHasher(PasswordHasherOptions options) => new(Options.Create(options));
}