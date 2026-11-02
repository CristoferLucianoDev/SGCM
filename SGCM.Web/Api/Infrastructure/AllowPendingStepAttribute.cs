using SGCM.Data.Domain.Identity.Users;

namespace SGCM.Web.Api.Infrastructure;

/// <summary>
/// Permite usar el endpoint a un usuario que todavía tiene ese paso pendiente. Sin este
/// atributo, <see cref="PendingStepFilter"/> responde 403 a quien tenga un paso por completar.
/// </summary>
/// <param name="step">Paso que este endpoint resuelve.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class AllowPendingStepAttribute(PendingStep step) : Attribute
{
    /// <summary>Paso que este endpoint resuelve.</summary>
    public PendingStep Step { get; } = step;
}
