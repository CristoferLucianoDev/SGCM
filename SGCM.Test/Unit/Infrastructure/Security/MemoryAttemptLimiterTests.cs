using Serilog.Core;
using Shouldly;
using SGCM.Data.Infrastructure.Security;
using SGCM.Test.Common.Fakes;

namespace SGCM.Test.Unit.Infrastructure.Security;

/// <summary>
/// Pruebas del conteo de intentos por clave en una ventana fija de <see cref="MemoryAttemptLimiter"/>.
/// </summary>
public sealed class MemoryAttemptLimiterTests
{
    private const string Key = "login:paciente@correo.com";
    private const string OtherKey = "login:otro@correo.com";
    private const int MaxAttempts = 5;
    private const int SmallCapacity = 3;

    private static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly FakeDateTimeProvider _clock = new();
    private readonly MemoryAttemptLimiter _limiter;

    public MemoryAttemptLimiterTests()
    {
        _limiter = new MemoryAttemptLimiter(_clock, Logger.None);
    }

    [Fact]
    public void Sin_intentos_no_supera_el_limite()
    {
        _limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeFalse();
    }

    [Fact]
    public void Con_menos_intentos_que_el_maximo_no_supera_el_limite()
    {
        RegisterAttempts(Key, MaxAttempts - 1);

        _limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeFalse();
    }

    [Fact]
    public void Al_llegar_al_maximo_supera_el_limite()
    {
        RegisterAttempts(Key, MaxAttempts);

        _limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeTrue();
    }

    [Fact]
    public void Al_terminar_la_ventana_el_limite_se_libera()
    {
        RegisterAttempts(Key, MaxAttempts);

        _clock.UtcNow += Window;

        _limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeFalse();
    }

    [Fact]
    public void Un_intento_despues_de_la_ventana_empieza_una_ventana_nueva()
    {
        RegisterAttempts(Key, MaxAttempts);
        _clock.UtcNow += Window;

        _limiter.RegisterAttempt(Key, Window);

        // Con un máximo de 1 se ve que la ventana nueva arrancó con un solo intento.
        _limiter.IsLimitExceeded(Key, 1).ShouldBeTrue();
        _limiter.IsLimitExceeded(Key, 2).ShouldBeFalse();
    }

    [Fact]
    public void Reset_borra_el_contador()
    {
        RegisterAttempts(Key, MaxAttempts);

        _limiter.Reset(Key);

        _limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeFalse();
    }

    [Fact]
    public void Las_claves_son_independientes()
    {
        RegisterAttempts(Key, MaxAttempts);

        _limiter.IsLimitExceeded(OtherKey, MaxAttempts).ShouldBeFalse();
    }

    [Fact]
    public void TryRegisterAttempt_por_debajo_del_maximo_registra_el_intento()
    {
        RegisterAttempts(Key, MaxAttempts - 1);

        _limiter.TryRegisterAttempt(Key, MaxAttempts, Window).ShouldBeTrue();

        _limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeTrue();
    }

    [Fact]
    public void TryRegisterAttempt_con_el_maximo_alcanzado_rechaza_sin_sumar()
    {
        RegisterAttempts(Key, MaxAttempts);

        _limiter.TryRegisterAttempt(Key, MaxAttempts, Window).ShouldBeFalse();

        // Si el rechazo hubiera sumado, el máximo de 6 ya estaría alcanzado.
        _limiter.IsLimitExceeded(Key, MaxAttempts + 1).ShouldBeFalse();
    }

    [Fact]
    public void TryRegisterAttempt_al_terminar_la_ventana_vuelve_a_registrar()
    {
        RegisterAttempts(Key, MaxAttempts);
        _clock.UtcNow += Window;

        _limiter.TryRegisterAttempt(Key, MaxAttempts, Window).ShouldBeTrue();
    }

    [Fact]
    public async Task TryRegisterAttempt_con_intentos_simultaneos_no_supera_el_maximo()
    {
        const int concurrentAttempts = 200;

        var results = await Task.WhenAll(Enumerable.Range(0, concurrentAttempts)
            .Select(_ => Task.Run(() => _limiter.TryRegisterAttempt(Key, MaxAttempts, Window))));

        results.Count(registered => registered).ShouldBe(MaxAttempts);
    }

    [Fact]
    public void Con_el_limitador_lleno_un_contador_al_maximo_sigue_superando_el_limite()
    {
        var limiter = new MemoryAttemptLimiter(_clock, Logger.None, SmallCapacity);
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            limiter.TryRegisterAttempt(Key, MaxAttempts, Window);
        }

        foreach (var newKey in NewKeys(SmallCapacity * 10))
        {
            limiter.TryRegisterAttempt(newKey, MaxAttempts, Window);
        }

        limiter.IsLimitExceeded(Key, MaxAttempts).ShouldBeTrue();
    }

    [Fact]
    public void Con_el_limitador_lleno_de_ventanas_vencidas_una_clave_nueva_se_registra()
    {
        var limiter = new MemoryAttemptLimiter(_clock, Logger.None, SmallCapacity);
        foreach (var oldKey in NewKeys(SmallCapacity))
        {
            limiter.RegisterAttempt(oldKey, Window);
        }

        _clock.UtcNow += Window;
        limiter.TryRegisterAttempt(Key, MaxAttempts, Window).ShouldBeTrue();

        limiter.IsLimitExceeded(Key, 1).ShouldBeTrue();
    }

    [Fact]
    public void Con_el_limitador_lleno_de_ventanas_vigentes_una_clave_nueva_se_permite_sin_guardarse()
    {
        var limiter = new MemoryAttemptLimiter(_clock, Logger.None, SmallCapacity);
        foreach (var activeKey in NewKeys(SmallCapacity))
        {
            limiter.RegisterAttempt(activeKey, Window);
        }

        limiter.TryRegisterAttempt(Key, MaxAttempts, Window).ShouldBeTrue();

        limiter.IsLimitExceeded(Key, 1).ShouldBeFalse();
    }

    private static IEnumerable<string> NewKeys(int count) =>
        Enumerable.Range(0, count).Select(index => $"login:usuario-{index}@correo.com");

    private void RegisterAttempts(string key, int count)
    {
        for (var attempt = 0; attempt < count; attempt++)
        {
            _limiter.RegisterAttempt(key, Window);
        }
    }
}
