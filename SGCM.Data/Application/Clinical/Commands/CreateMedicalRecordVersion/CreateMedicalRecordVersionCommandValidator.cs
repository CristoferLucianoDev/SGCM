using FluentValidation;
using SGCM.Data.Domain.Clinical.MedicalRecords;

namespace SGCM.Data.Application.Clinical.Commands.CreateMedicalRecordVersion;

/// <summary>
/// Valida la forma de <see cref="CreateMedicalRecordVersionCommand"/>: el identificador de la cita debe ser mayor que
/// cero, al menos una parte debe tener texto y cada una, recortada, debe respetar su longitud máxima. Los códigos son
/// los de <see cref="MedicalRecordErrors"/>, los mismos que devuelve el dominio.
/// </summary>
public sealed class CreateMedicalRecordVersionCommandValidator : AbstractValidator<CreateMedicalRecordVersionCommand>
{
    public CreateMedicalRecordVersionCommandValidator()
    {
        RuleFor(command => command.AppointmentId)
            .GreaterThan(0)
            .WithErrorCode("Appointment.InvalidAppointmentId")
            .WithMessage("El identificador de la cita debe ser mayor que cero.");

        RuleFor(command => command)
            .Must(command => HasText(command.Diagnosis) || HasText(command.Treatment) || HasText(command.Notes))
            .WithErrorCode(MedicalRecordErrors.Empty.Code)
            .WithMessage(MedicalRecordErrors.Empty.Description);

        RuleFor(command => command.Diagnosis)
            .Must(value => FitsIn(value, MedicalRecordContent.DiagnosisMaxLength))
            .WithErrorCode(MedicalRecordErrors.DiagnosisTooLong.Code)
            .WithMessage(MedicalRecordErrors.DiagnosisTooLong.Description);

        RuleFor(command => command.Treatment)
            .Must(value => FitsIn(value, MedicalRecordContent.TreatmentMaxLength))
            .WithErrorCode(MedicalRecordErrors.TreatmentTooLong.Code)
            .WithMessage(MedicalRecordErrors.TreatmentTooLong.Description);

        RuleFor(command => command.Notes)
            .Must(value => FitsIn(value, MedicalRecordContent.NotesMaxLength))
            .WithErrorCode(MedicalRecordErrors.NotesTooLong.Code)
            .WithMessage(MedicalRecordErrors.NotesTooLong.Description);
    }

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);

    private static bool FitsIn(string? value, int maxLength) => value is null || value.Trim().Length <= maxLength;
}
