using FluentValidation;

namespace SGCM.Data.Application.Clinical.Queries.GetMedicalHistory;

/// <summary>Valida la forma de <see cref="GetMedicalHistoryQuery"/>: si viene un paciente, su id debe ser mayor que cero.</summary>
public sealed class GetMedicalHistoryQueryValidator : AbstractValidator<GetMedicalHistoryQuery>
{
    public GetMedicalHistoryQueryValidator()
    {
        RuleFor(query => query.PatientId)
            .GreaterThan(0)
            .When(query => query.PatientId is not null)
            .WithErrorCode("MedicalHistory.InvalidPatientId")
            .WithMessage("El identificador del paciente debe ser mayor que cero.");
    }
}
