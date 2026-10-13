using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Domain.Identity.Users;
using IdentityPasswordHasher = Microsoft.AspNetCore.Identity.PasswordHasher<object>;
using PasswordVerificationResult = Microsoft.AspNetCore.Identity.PasswordVerificationResult;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// PBKDF2 del PasswordHasher de ASP.NET Core Identity (formato V3), usado sin el resto de Identity.
/// </summary>
internal sealed class PasswordHasher : IPasswordHasher
{
    // La implementación de Identity no usa el usuario para calcular el hash.
    private static readonly object NoUser = new();

    // El texto no importa: nadie lo conoce como contraseña y el resultado siempre se descarta.
    private const string DummyPassword = "relleno-para-correos-inexistentes";

    // Lazy porque calcular un hash cuesta lo mismo que verificarlo y solo hace falta una vez.
    private static readonly Lazy<string> DummyHash = new(() =>
        new IdentityPasswordHasher().HashPassword(NoUser, DummyPassword));

    private readonly IdentityPasswordHasher _inner = new();

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
            _inner.VerifyHashedPassword(NoUser, DummyHash.Value, password);
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