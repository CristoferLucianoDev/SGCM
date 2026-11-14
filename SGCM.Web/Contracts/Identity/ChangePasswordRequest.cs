namespace SGCM.Web.Contracts.Identity;

/// <summary>Contraseña actual y nueva; la identidad de usuario y sesión procede exclusivamente del JWT.</summary>
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword)
{
    public override string ToString() =>
        $"{nameof(ChangePasswordRequest)} {{ CurrentPassword = ********, NewPassword = ******** }}";
}
