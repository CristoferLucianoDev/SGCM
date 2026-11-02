namespace SGCM.Data.Domain.People.Doctors;

/// <summary>Vínculo inmutable que pertenece al agregado Doctor, identificado por la especialidad.</summary>
public sealed class DoctorSpecialty
{
    public int SpecialtyId { get; }

    internal DoctorSpecialty(int specialtyId)
    {
        SpecialtyId = specialtyId;
    }
}
