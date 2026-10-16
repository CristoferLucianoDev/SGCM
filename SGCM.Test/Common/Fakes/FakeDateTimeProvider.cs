using SGCM.Data.Application.Abstractions.Services;
using SGCM.Test.Common.Mothers;

namespace SGCM.Test.Common.Fakes;

/// <summary>
/// Reloj controlado por la prueba. Por defecto marca el mismo "ahora" que <see cref="UserMother"/>.
/// </summary>
public sealed class FakeDateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = UserMother.Now;

    /// <summary>
    /// Vuelve el reloj a <see cref="UserMother.Now"/>.
    /// </summary>
    public void Reset() => UtcNow = UserMother.Now;
}