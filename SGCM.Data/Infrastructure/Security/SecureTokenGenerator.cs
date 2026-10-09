using System.Security.Cryptography;
using System.Text;
using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Data.Infrastructure.Security;

internal sealed class SecureTokenGenerator : ISecureTokenGenerator
{
    private const int TokenSizeInBytes = 32;

    public SecureToken Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenSizeInBytes);

        // Base64Url: el token viaja en un enlace, así que no puede llevar '+', '/' ni '='.
        var value = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return new SecureToken(value, ComputeHash(value));
    }

    private static string ComputeHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}