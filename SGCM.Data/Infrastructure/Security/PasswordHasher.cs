using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Identity.Users;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;
using PasswordHasherOptions = Microsoft.AspNetCore.Identity.PasswordHasherOptions;
using PasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// PBKDF2 del PasswordHasher de ASP.NET Core Identity (formato V3), usado sin el resto de Identity.
/// </summary>
/// <remarks>
/// Las iteraciones de PBKDF2 vienen de <see cref="PasswordHasherOptions"/>. Producción no registra
/// esas opciones y usa el valor por defecto de Identity. El formato V3 guarda las iteraciones dentro
/// de cada hash, así que los hashes existentes se siguen verificando aunque cambie el valor configurado.
/// </remarks>
internal sealed class PasswordHasher : IPasswordHasher
{
    // La implementación de Identity no usa el usuario para calcular el hash.
    private static readonly object NoUser = new();

    // El texto no importa: nadie lo conoce como contraseña y el resultado siempre se descarta.
    private const string DummyPassword = "relleno-para-correos-inexistentes";

    private readonly IdentityPasswordHasher _inner;

    // Lazy porque calcular un hash cuesta lo mismo que verificarlo y solo hace falta una vez. Sale del
    // mismo hasher para tener las mismas iteraciones que los hashes reales: así verificar un correo
    // inexistente tarda lo mismo que verificar uno real.
    private readonly Lazy<string> _dummyHash;

    /// <summary>
    /// Crea el hasher con las opciones de PasswordHasher de Identity.
    /// </summary>
    /// <param name="options">Opciones de Identity; definen las iteraciones de PBKDF2 de los hashes nuevos.</param>
    /// <exception cref="ArgumentNullException">Si <paramref name="options"/> es nulo.</exception>
    public PasswordHasher(IOptions<PasswordHasherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _inner = new IdentityPasswordHasher(options);
        _dummyHash = new Lazy<string>(() => _inner.HashPassword(NoUser, DummyPassword));
    }

    public string Hash(Password password)
    {
        ArgumentNullException.ThrowIfNull(password);
        return _inner.HashPassword(NoUser, password.Value);
    }

    public bool Verify(string? passwordHash, string password)
    {
        if (passwordHash is null)
        {
            // Se verifica igual, para gastar el mismo tiempo que con un usuario real.
            _inner.VerifyHashedPassword(NoUser, _dummyHash.Value, password);
            return false;
        }

        try
        {
            var result = _inner.VerifyHashedPassword(NoUser, passwordHash, password);

            // El rehash cuando cambian los parámetros de Identity queda fuera por ahora;
            // la contraseña es correcta igual.
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (FormatException)
        {
            // Identity decodifica el hash como Base64 y lanza si no lo es; un hash dañado no autentica.
            return false;
        }
    }
}