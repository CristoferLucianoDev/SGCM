using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Messaging.ReminderTemplates;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Messaging;

public sealed class ReminderTemplateTests
{
    private const int DoctorId = 7;
    private const int OtherDoctorId = 8;

    [Fact]
    public void Una_plantilla_global_activa_la_puede_usar_cualquier_medico()
    {
        var template = ReminderTemplateMother.Global();

        template.IsUsableBy(DoctorId).ShouldBeTrue();
        template.IsUsableBy(OtherDoctorId).ShouldBeTrue();
    }

    [Fact]
    public void Una_plantilla_privada_activa_solo_la_usa_su_medico()
    {
        var template = ReminderTemplateMother.Create(5, DoctorId, "Seguimiento", "Asunto", "Texto");

        template.IsUsableBy(DoctorId).ShouldBeTrue();
        template.IsUsableBy(OtherDoctorId).ShouldBeFalse();
    }

    [Fact]
    public void Una_plantilla_inactiva_no_la_usa_nadie()
    {
        ReminderTemplateMother.Global(isActive: false).IsUsableBy(DoctorId).ShouldBeFalse();
        ReminderTemplateMother.Create(5, DoctorId, "Seguimiento", "Asunto", "Texto", isActive: false)
            .IsUsableBy(DoctorId).ShouldBeFalse();
    }

    [Fact]
    public void El_medico_crea_una_plantilla_activa_propia_con_los_datos_recortados()
    {
        var result = ReminderTemplate.Create(DoctorId, "  Indicaciones  ", "  Su cita de {SpecialtyName}  ", "  Hola {PatientName}.  ");

        var template = result.Value;
        template.DoctorId.ShouldBe(DoctorId);
        template.Name.ShouldBe("Indicaciones");
        template.Subject.ShouldBe("Su cita de {SpecialtyName}");
        template.BodyTemplate.ShouldBe("Hola {PatientName}.");
        template.IsActive.ShouldBeTrue();
        template.IsGlobal.ShouldBeFalse();
        template.IsOwnedBy(DoctorId).ShouldBeTrue();
        template.IsOwnedBy(OtherDoctorId).ShouldBeFalse();
    }

    [Fact]
    public void Admite_todas_las_variables_permitidas_en_el_asunto_y_el_cuerpo()
    {
        var all = string.Join(" ", ReminderTemplate.AllowedVariables.Select(name => $"{{{name}}}"));

        ReminderTemplate.Create(DoctorId, "Todas", all, all).IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Un_medico_sin_id_valido_es_un_error_de_programacion(int doctorId)
    {
        Should.Throw<DomainException>(() => ReminderTemplate.Create(doctorId, "Nombre", "Asunto", "Cuerpo"));
    }

    public static IEnumerable<object?[]> InvalidData()
    {
        yield return [null, "Asunto", "Cuerpo", ReminderTemplateErrors.NameRequired];
        yield return ["   ", "Asunto", "Cuerpo", ReminderTemplateErrors.NameRequired];
        yield return [new string('A', ReminderTemplate.NameMaxLength + 1), "Asunto", "Cuerpo", ReminderTemplateErrors.NameTooLong];
        yield return ["Nombre", null, "Cuerpo", ReminderTemplateErrors.SubjectRequired];
        yield return ["Nombre", new string('A', ReminderTemplate.SubjectMaxLength + 1), "Cuerpo", ReminderTemplateErrors.SubjectTooLong];
        yield return ["Nombre", "Asunto", "  ", ReminderTemplateErrors.BodyRequired];
        yield return ["Nombre", "Asunto", new string('A', ReminderTemplate.BodyTemplateMaxLength + 1), ReminderTemplateErrors.BodyTooLong];
    }

    [Theory]
    [MemberData(nameof(InvalidData))]
    public void Nombre_asunto_y_cuerpo_son_obligatorios_y_tienen_longitud_maxima(
        string? name, string? subject, string? body, Error error)
    {
        var result = ReminderTemplate.Create(DoctorId, name, subject, body);

        result.Error.ShouldBe(error);
        result.Error.Type.ShouldBe(ErrorType.Validation);
    }

    [Fact]
    public void Las_longitudes_maximas_se_aceptan()
    {
        ReminderTemplate.Create(
                DoctorId,
                new string('A', ReminderTemplate.NameMaxLength),
                new string('A', ReminderTemplate.SubjectMaxLength),
                new string('A', ReminderTemplate.BodyTemplateMaxLength))
            .IsSuccess.ShouldBeTrue();
    }

    [Theory]
    [InlineData("Asunto", "Hola {PatientName}, su cita con {Doctor}.", "Doctor")]
    [InlineData("Su cita de {Especialidad}", "Hola.", "Especialidad")]
    [InlineData("Asunto", "Hola {Patient Name}.", "Patient Name")]
    public void Una_variable_no_permitida_se_rechaza_indicandola(string subject, string body, string variable)
    {
        var result = ReminderTemplate.Create(DoctorId, "Nombre", subject, body);

        result.Error.Code.ShouldBe("ReminderTemplate.UnknownVariable");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Description.ShouldStartWith($"La variable {{{variable}}} no está permitida.");
        result.Error.Description.ShouldContain("{PatientName}, {DoctorName}, {AppointmentDate}, {StartTime}, {SpecialtyName}");
    }

    [Fact]
    public void El_medico_edita_su_plantilla_con_las_mismas_reglas()
    {
        var template = ReminderTemplateMother.Own(DoctorId);

        template.Update(" Nuevo nombre ", "Nuevo asunto", "Hola {PatientName}, texto nuevo.").IsSuccess.ShouldBeTrue();

        template.Name.ShouldBe("Nuevo nombre");
        template.Subject.ShouldBe("Nuevo asunto");
        template.BodyTemplate.ShouldBe("Hola {PatientName}, texto nuevo.");
    }

    [Fact]
    public void Una_edicion_invalida_no_cambia_nada()
    {
        var template = ReminderTemplateMother.Own(DoctorId);

        template.Update("Nuevo nombre", "Nuevo asunto", "Hola {Paciente}.").Error.Code.ShouldBe("ReminderTemplate.UnknownVariable");

        template.Name.ShouldBe(ReminderTemplateMother.OwnName);
        template.Subject.ShouldBe(ReminderTemplateMother.OwnSubject);
        template.BodyTemplate.ShouldBe(ReminderTemplateMother.OwnBody);
    }

    [Fact]
    public void Una_plantilla_global_no_se_edita_ni_se_desactiva_ni_se_activa()
    {
        var active = ReminderTemplateMother.Global();
        var inactive = ReminderTemplateMother.Global(isActive: false);

        active.Update("Otro", "Otro", "Otro").Error.ShouldBe(ReminderTemplateErrors.GlobalReadOnly);
        active.Deactivate().Error.ShouldBe(ReminderTemplateErrors.GlobalReadOnly);
        inactive.Activate().Error.ShouldBe(ReminderTemplateErrors.GlobalReadOnly);
        active.Name.ShouldBe(ReminderTemplate.AutomaticReminderName);
        active.IsActive.ShouldBeTrue();
        inactive.IsActive.ShouldBeFalse();
        ReminderTemplateErrors.GlobalReadOnly.Type.ShouldBe(ErrorType.Forbidden);
    }

    [Fact]
    public void El_medico_desactiva_su_plantilla_una_sola_vez()
    {
        var template = ReminderTemplateMother.Own(DoctorId);

        template.Deactivate().IsSuccess.ShouldBeTrue();

        template.IsActive.ShouldBeFalse();
        template.IsUsableBy(DoctorId).ShouldBeFalse();
        template.Deactivate().Error.Code.ShouldBe("ReminderTemplate.AlreadyInactive");
    }
}
