namespace SGCM.Web.Contracts.Common;

/// <summary>
/// Identificación declarada del cliente en login. No acredita procedencia ni concede permisos.
/// La ausencia conserva el comportamiento web; valores vacíos, desconocidos o múltiples se rechazan.
/// </summary>
public static class ClientTypeHeader
{
    public const string Name = "X-SGCM-Client";
    public const string Web = "web";
    public const string Mobile = "mobile";
}
