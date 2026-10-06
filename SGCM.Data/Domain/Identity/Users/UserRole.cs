namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Rol de un usuario. Los valores numéricos son explícitos porque se persisten.
/// </summary>
public enum UserRole
{
    /// <summary>Paciente que reserva y consulta sus citas. Es el rol del autorregistro.</summary>
    Patient = 1,

    /// <summary>Médico que atiende citas.</summary>
    Doctor = 2,

    /// <summary>Administrador del sistema.</summary>
    Administrator = 3
}
