namespace SGCM.Web.Contracts.Identity;

/// <summary>Contraseña candidata; la identidad de usuario y sesión procede exclusivamente del JWT.</summary>
public sealed record ChangeTemporaryPasswordRequest(string NewPassword)
{
    public override string ToString() => $"{nameof(ChangeTemporaryPasswordRequest)} {{ NewPassword = ******** }}";
}
