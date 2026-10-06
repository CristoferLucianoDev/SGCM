using SGCM.Data.Domain.Common.Interfaces;

namespace SGCM.Data.Domain.Identity.Users.Events;

/// <summary>
/// Se emitió un token para un usuario. Su manejador envía el correo con el enlace
/// que contiene <see cref="RawToken"/>, el único lugar donde viaja el token en claro
/// (en la base solo se guarda su hash).
/// </summary>
/// <param name="Email">Correo al que se envía el token.</param>
/// <param name="Type">Propósito del token.</param>
/// <param name="RawToken">Token en claro. No debe registrarse en logs ni persistirse.</param>
public sealed record UserTokenIssuedEvent(string Email, UserTokenType Type, string RawToken) : IDomainEvent
{
    /// <summary>
    /// Representación del evento que oculta el token, para que no se filtre si el
    /// evento termina en un log.
    /// </summary>
    public override string ToString() =>
        $"{nameof(UserTokenIssuedEvent)} {{ Email = {Email}, Type = {Type}, RawToken = ******** }}";
}
