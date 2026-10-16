using System.Security.Cryptography;
using SGCM.Data.Application.Abstractions.Security;

namespace SGCM.Data.Infrastructure.Security;

/// <summary>
/// Contraseñas temporales de 12 caracteres con al menos una mayúscula, una minúscula,
/// un dígito y un símbolo. Excluye los caracteres que se confunden al dictarlos o
/// copiarlos (0, O, o, 1, l, I).
/// </summary>
internal sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    public const int Length = 12;

    internal const string Uppercase = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    internal const string Lowercase = "abcdefghijkmnpqrstuvwxyz";
    internal const string Digits = "23456789";
    internal const string Symbols = "#$%&*+-=?@";

    private const string AllCharacters = Uppercase + Lowercase + Digits + Symbols;

    public string Generate()
    {
        var characters = new char[Length];

        // Uno de cada tipo garantiza la mezcla; el resto sale del conjunto completo.
        characters[0] = Pick(Uppercase);
        characters[1] = Pick(Lowercase);
        characters[2] = Pick(Digits);
        characters[3] = Pick(Symbols);

        for (var i = 4; i < Length; i++)
        {
            characters[i] = Pick(AllCharacters);
        }

        Shuffle(characters);

        return new string(characters);
    }

    // GetInt32 elige sin sesgo; nunca se usa System.Random para secretos.
    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];

    // Fisher-Yates, para que los caracteres obligatorios no queden siempre al principio.
    private static void Shuffle(char[] characters)
    {
        for (var i = characters.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }
    }
}