using SGCM.Data.Domain.Common;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Common;

/// <summary>
/// Pruebas de la coherencia entre estado y error en <see cref="Result"/> y <see cref="Result{T}"/>.
/// </summary>
public class ResultTests
{
    private static readonly Error SomeError = Error.Validation("Test.Error", "Error de prueba.");

    [Fact]
    public void Success_no_tiene_error()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_conserva_el_error()
    {
        var result = Result.Failure(SomeError);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SomeError);
    }

    [Fact]
    public void Failure_sin_error_lanza_ArgumentException()
    {
        Should.Throw<ArgumentException>(() => Result.Failure(Error.None));
        Should.Throw<ArgumentException>(() => Result<int>.Failure(Error.None));
    }

    [Fact]
    public void Value_de_un_resultado_fallido_lanza_InvalidOperationException()
    {
        Result<int> result = SomeError;

        Should.Throw<InvalidOperationException>(() => result.Value);
    }
}
