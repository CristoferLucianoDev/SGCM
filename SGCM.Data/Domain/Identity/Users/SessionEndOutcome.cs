namespace SGCM.Data.Domain.Identity.Users;

/// <summary>
/// Resultado de <see cref="User.EndSession"/>: si se cerró una sesión y con qué hash.
/// </summary>
public enum SessionEndOutcome
{
    /// <summary>No se cerró ninguna sesión: el hash no coincide o la sesión ya no estaba activa.</summary>
    None,

    /// <summary>Se cerró la sesión con su hash vigente.</summary>
    ClosedByCurrentToken,

    /// <summary>Se cerró la sesión con el hash anterior a su última rotación.</summary>
    ClosedByPreviousToken
}
