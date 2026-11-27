using SGCM.Data.Domain.Clinical.MedicalRecords;
using SGCM.Data.Domain.Common.Exceptions;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Clinical.MedicalRecords;

/// <summary>
/// Pruebas de <see cref="MedicalRecordAccessLog"/>: quién accedió a qué versión, cómo, cuándo y desde qué IP.
/// </summary>
public sealed class MedicalRecordAccessLogTests
{
    private const int MedicalRecordId = 7;
    private const int UserId = 30;

    private static readonly DateTimeOffset AccessedAt = new(2026, 11, 2, 14, 30, 0, TimeSpan.FromHours(-4));

    [Fact]
    public void Registra_la_version_el_usuario_el_tipo_y_la_hora_en_utc()
    {
        var log = MedicalRecordAccessLog.Record(
            MedicalRecordId, UserId, MedicalRecordAccessType.Create, AccessedAt, "192.168.1.20");

        log.MedicalRecordId.ShouldBe(MedicalRecordId);
        log.AccessedBy.ShouldBe(UserId);
        log.AccessType.ShouldBe(MedicalRecordAccessType.Create);
        log.AccessedAt.ShouldBe(AccessedAt);
        log.AccessedAt.Offset.ShouldBe(TimeSpan.Zero);
        log.IpAddress.ShouldBe("192.168.1.20");
        log.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void Guarda_la_ipv6_en_su_forma_canonica()
    {
        MedicalRecordAccessLog.Record(
                MedicalRecordId, UserId, MedicalRecordAccessType.Update, AccessedAt, "2001:0DB8:0000:0000:0000:0000:0000:0001")
            .IpAddress.ShouldBe("2001:db8::1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-es-una-ip")]
    [InlineData("fe80:1111:2222:3333:4444:5555:6666:7777%4294967295")]
    public void Una_ip_ausente_invalida_o_que_no_cabe_se_guarda_como_null(string? ipAddress)
    {
        MedicalRecordAccessLog.Record(MedicalRecordId, UserId, MedicalRecordAccessType.View, AccessedAt, ipAddress)
            .IpAddress.ShouldBeNull();
    }

    [Theory]
    [InlineData(0, UserId, MedicalRecordAccessType.Create)]
    [InlineData(MedicalRecordId, 0, MedicalRecordAccessType.Create)]
    [InlineData(MedicalRecordId, UserId, (MedicalRecordAccessType)99)]
    public void Datos_invalidos_son_un_error_de_programacion(
        int medicalRecordId, int accessedBy, MedicalRecordAccessType accessType)
    {
        Should.Throw<DomainException>(() =>
            MedicalRecordAccessLog.Record(medicalRecordId, accessedBy, accessType, AccessedAt, ipAddress: null));
    }
}
