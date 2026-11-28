using SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;
using SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;
using SGCM.Data.Domain.Scheduling.Appointments;
using SGCM.Web.Contracts.Clinical;
using SGCM.Web.Mappers.Clinical;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class MedicalRecordMapperTests
{
    [Fact]
    public void ToCommand_lleva_la_cita_el_contenido_y_la_ip()
    {
        var request = new CreateMedicalRecordVersionRequest("Faringitis", "Reposo", "Control");

        request.ToCommand(99, "10.0.0.5").ShouldBe(
            new CreateMedicalRecordVersionCommand(99, "Faringitis", "Reposo", "Control", "10.0.0.5"));
    }

    [Fact]
    public void ToResponse_copia_todos_los_campos()
    {
        var createdAt = new DateTimeOffset(2027, 1, 12, 13, 30, 0, TimeSpan.Zero);
        var result = new MedicalRecordVersionResult(501, 99, "Faringitis", null, "Control", createdAt, IsCorrection: true);

        result.ToResponse().ShouldBe(
            new MedicalRecordVersionResponse(501, 99, "Faringitis", null, "Control", createdAt, IsCorrection: true));
    }

    [Fact]
    public void Los_mapeos_rechazan_nulos()
    {
        Should.Throw<ArgumentNullException>(() => ((CreateMedicalRecordVersionRequest)null!).ToCommand(99, null));
        Should.Throw<ArgumentNullException>(() => ((MedicalRecordVersionResult)null!).ToResponse());
        Should.Throw<ArgumentNullException>(() => ((MedicalHistoryDto)null!).ToResponse());
    }

    [Fact]
    public void ToResponse_del_historial_copia_las_citas_las_versiones_y_el_estado_en_minusculas()
    {
        var startsAt = new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.Zero);
        var current = new MedicalRecordVersionDto(11, "Amigdalitis", "Antibiótico", null, startsAt.AddHours(3));
        var previous = new MedicalRecordVersionDto(10, "Faringitis", null, "Control", startsAt.AddHours(1));
        var history = new MedicalHistoryDto(20,
        [
            new MedicalHistoryEntryDto(1, startsAt, startsAt.AddHours(2), AppointmentStatus.Completed, 3, "Ana Pérez",
                "Medicina general", current, [previous]),
            new MedicalHistoryEntryDto(2, startsAt.AddDays(-7), startsAt.AddDays(-7).AddHours(2),
                AppointmentStatus.Completed, 4, "Luis Gómez", "Pediatría", null, [])
        ]);

        var response = history.ToResponse();

        response.PatientId.ShouldBe(20);
        var first = response.Entries[0];
        first.AppointmentId.ShouldBe(1);
        first.StartsAt.ShouldBe(startsAt);
        first.EndsAt.ShouldBe(startsAt.AddHours(2));
        first.Status.ShouldBe("completed");
        first.DoctorId.ShouldBe(3);
        first.DoctorName.ShouldBe("Ana Pérez");
        first.SpecialtyName.ShouldBe("Medicina general");
        first.HasMedicalRecord.ShouldBeTrue();
        first.CurrentVersion.ShouldBe(
            new MedicalHistoryVersionResponse(11, "Amigdalitis", "Antibiótico", null, startsAt.AddHours(3)));
        first.PreviousVersions.ShouldBe(
            [new MedicalHistoryVersionResponse(10, "Faringitis", null, "Control", startsAt.AddHours(1))]);
        var second = response.Entries[1];
        second.HasMedicalRecord.ShouldBeFalse();
        second.CurrentVersion.ShouldBeNull();
        second.PreviousVersions.ShouldBeEmpty();
    }
}
