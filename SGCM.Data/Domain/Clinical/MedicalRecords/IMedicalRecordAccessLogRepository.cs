using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Clinical.MedicalRecords;

/// <summary>
/// Repositorio del registro de accesos al registro clínico. Solo agrega filas: el registro no se consulta para
/// modificarlo ni se borra.
/// </summary>
public interface IMedicalRecordAccessLogRepository : IRepository<MedicalRecordAccessLog>;
