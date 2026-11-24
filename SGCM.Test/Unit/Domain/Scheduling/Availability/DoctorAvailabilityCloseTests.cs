using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Scheduling;
using SGCM.Data.Domain.Scheduling.Availability;
using SGCM.Data.Domain.Scheduling.Availability.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Scheduling.Availability;

public sealed class DoctorAvailabilityCloseTests
{
    private const int DoctorId = DoctorAvailabilitySeriesMother.DoctorId;
    private const int BlockId = 40;
    private const int MinimumHours = DoctorAvailabilitySeriesMother.MinimumDurationHours;
    private static readonly TimeSpan SantoDomingo = TimeSpan.FromHours(-4);

    // El bloque es el martes 5 de enero de 2027 de 8:00 a 14:00 en Santo Domingo (12:00 a 18:00 UTC).
    private static readonly DateTimeOffset Start = new(2027, 1, 5, 8, 0, 0, SantoDomingo);
    private static readonly DateTimeOffset End = Start.AddHours(MinimumHours);

    [Fact]
    public void Cerrar_deja_el_bloque_no_disponible_y_emite_el_evento_con_el_rango_y_el_motivo()
    {
        var block = Block();

        var result = block.Close("Congreso médico", Start.AddDays(-30));

        result.IsSuccess.ShouldBeTrue();
        block.IsAvailable.ShouldBeFalse();
        block.StartsAt.ShouldBe(Start);
        block.EndsAt.ShouldBe(End);
        block.DomainEvents.ShouldHaveSingleItem().ShouldBe(
            new DoctorAvailabilityClosedEvent(BlockId, DoctorId, TimeRange.Create(Start, End).Value, "Congreso médico"));
    }

    [Fact]
    public void Se_puede_cerrar_un_bloque_de_la_semana_actual_incluso_el_mismo_dia()
    {
        var block = Block();

        block.Close(null, Start.AddHours(-1)).IsSuccess.ShouldBeTrue();
        block.IsAvailable.ShouldBeFalse();
    }

    [Fact]
    public void Se_puede_cerrar_un_bloque_que_ya_empezo_pero_no_ha_terminado()
    {
        var block = Block();

        block.Close(null, Start.AddHours(3)).IsSuccess.ShouldBeTrue();
        block.IsAvailable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Sin_motivo_el_evento_lleva_null(string? reason)
    {
        var block = Block();

        block.Close(reason, Start.AddDays(-1));

        block.DomainEvents.OfType<DoctorAvailabilityClosedEvent>().ShouldHaveSingleItem().Reason.ShouldBeNull();
    }

    [Fact]
    public void El_motivo_viaja_recortado()
    {
        var block = Block();

        block.Close("  Viaje  ", Start.AddDays(-1));

        block.DomainEvents.OfType<DoctorAvailabilityClosedEvent>().ShouldHaveSingleItem().Reason.ShouldBe("Viaje");
    }

    [Fact]
    public void Un_bloque_ya_cerrado_no_se_vuelve_a_cerrar_ni_emite_otro_evento()
    {
        var block = Block();
        block.Close(null, Start.AddDays(-1));
        block.ClearDomainEvents();

        var result = block.Close("Otra vez", Start.AddDays(-1));

        AssertRejected(result, "Availability.AlreadyClosed");
        block.DomainEvents.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void Un_bloque_que_ya_termino_no_se_puede_cerrar(int hoursAfterEnd)
    {
        var block = Block();

        var result = block.Close(null, End.AddHours(hoursAfterEnd));

        AssertRejected(result, "Availability.AlreadyEnded");
        block.IsAvailable.ShouldBeTrue();
        block.DomainEvents.ShouldBeEmpty();
    }

    private static void AssertRejected(Result result, string code)
    {
        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    private static DoctorAvailability Block()
    {
        var block = DoctorAvailability.Create(DoctorId, 15, Start, End, MinimumHours).Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(block, BlockId);
        return block;
    }
}

public sealed class DoctorAvailabilityValidateCloseTests
{
    private static readonly DateTimeOffset Start = new(2027, 1, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Un_bloque_abierto_que_no_termino_se_puede_cerrar_sin_cambiarlo()
    {
        var block = Block();

        block.ValidateClose(Start.AddHours(-1)).ShouldBe(Error.None);
        block.IsAvailable.ShouldBeTrue();
        block.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Devuelve_los_mismos_errores_que_Close()
    {
        Block().ValidateClose(Start.AddHours(6)).ShouldBe(DoctorAvailabilityErrors.AlreadyEnded);

        var closed = Block();
        closed.Close(null, Start.AddHours(-1));
        closed.ValidateClose(Start.AddHours(-1)).ShouldBe(DoctorAvailabilityErrors.AlreadyClosed);
    }

    private static DoctorAvailability Block() =>
        DoctorAvailability.Create(DoctorAvailabilitySeriesMother.DoctorId, 15, Start, Start.AddHours(6), 6).Value;
}
