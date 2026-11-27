using SGCM.Data.Application.Clinical;
using SGCM.Data.Domain.Clinical.MedicalRecords;
using Shouldly;

namespace SGCM.Test.Unit.Application.Clinical;

/// <summary>Pruebas de <see cref="MedicalRecordAccessTracker"/>.</summary>
public sealed class MedicalRecordAccessTrackerTests
{
    private static readonly MedicalRecord Record = MedicalRecord.Create(
        10, 20, MedicalRecordContent.Create("Faringitis", null, null).Value, 30, DateTimeOffset.UnixEpoch);

    [Fact]
    public void Empieza_sin_accesos()
    {
        new MedicalRecordAccessTracker().Accesses.ShouldBeEmpty();
    }

    [Fact]
    public void Guarda_los_accesos_en_el_orden_informado()
    {
        var tracker = new MedicalRecordAccessTracker();
        var correction = Record.Correct(MedicalRecordContent.Create("Gripe", null, null).Value, 30, DateTimeOffset.UnixEpoch);

        tracker.Track(Record, MedicalRecordAccessType.View);
        tracker.Track(correction, MedicalRecordAccessType.Update);

        tracker.Accesses.ShouldBe(
        [
            new MedicalRecordAccess(Record, MedicalRecordAccessType.View),
            new MedicalRecordAccess(correction, MedicalRecordAccessType.Update)
        ]);
    }

    [Fact]
    public void Clear_descarta_los_accesos()
    {
        var tracker = new MedicalRecordAccessTracker();
        tracker.Track(Record, MedicalRecordAccessType.Create);

        tracker.Clear();

        tracker.Accesses.ShouldBeEmpty();
    }

    [Fact]
    public void No_acepta_una_version_nula()
    {
        Should.Throw<ArgumentNullException>(() =>
            new MedicalRecordAccessTracker().Track(null!, MedicalRecordAccessType.View));
    }
}
