namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>Tipo de acceso a un registro clínico, como lo guarda la columna <c>access_type</c>.</summary>
public enum MedicalRecordAccessType
{
    /// <summary>Consulta del registro.</summary>
    View,

    /// <summary>Creación de la primera versión del registro de una cita.</summary>
    Create,

    /// <summary>Corrección: creación de una versión nueva sobre una existente.</summary>
    Update
}
