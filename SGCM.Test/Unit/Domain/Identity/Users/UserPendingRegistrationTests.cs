using SGCM.Data.Domain.Common.Exceptions;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.Identity.Users.Events;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Domain.Identity.Users;

/// <summary>
/// Pruebas del registro pendiente de un paciente: <see cref="User.RegisterPending"/>
/// y <see cref="User.ReplacePendingRegistration"/>.
/// </summary>
public class UserPendingRegistrationTests
{
    private static readonly DateTimeOffset Now = UserMother.Now;
    private static readonly TimeSpan Lifetime = UserMother.ConfirmationLifetime;

    [Fact]
    public void RegisterPending_crea_una_cuenta_pendiente_con_rol_Patient()
    {
        var user = UserMother.Pending();

        user.Email.ShouldBe(UserMother.DefaultEmail);
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
        user.Role.ShouldBe(UserRole.Patient);
        user.IsEmailConfirmed.ShouldBeFalse();
        user.MustChangePassword.ShouldBeFalse();
        user.IsActive.ShouldBeTrue();
    }

    [Fact]
    public void RegisterPending_emite_un_token_de_confirmacion_vigente()
    {
        var user = UserMother.Pending();

        var token = user.Tokens.ShouldHaveSingleItem();
        token.Type.ShouldBe(UserTokenType.EmailConfirmation);
        token.TokenHash.ShouldBe(UserMother.DefaultTokenHash);
        token.IssuedAt.ShouldBe(Now);
        token.ExpiresAt.ShouldBe(Now + Lifetime);
        token.IsValidAt(Now).ShouldBeTrue();
    }

    [Fact]
    public void RegisterPending_dispara_el_evento_que_envia_el_correo()
    {
        var user = UserMother.Pending();

        var domainEvent = user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserTokenIssuedEvent>();
        domainEvent.Email.ShouldBe(UserMother.DefaultEmail.Value);
        domainEvent.Type.ShouldBe(UserTokenType.EmailConfirmation);
        domainEvent.RawToken.ShouldBe(UserMother.DefaultRawToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterPending_sin_hash_de_contrasena_lanza_DomainException(string? passwordHash)
    {
        Should.Throw<DomainException>(() => User.RegisterPending(
            UserMother.DefaultEmail, passwordHash!, UserMother.DefaultRawToken, UserMother.DefaultTokenHash, Now, Lifetime));
    }

    [Fact]
    public void ReplacePendingRegistration_cambia_la_contrasena()
    {
        var user = UserMother.Pending();

        var result = user.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", Now.AddMinutes(5), Lifetime);

        result.IsSuccess.ShouldBeTrue();
        user.PasswordHash.ShouldBe("nuevo-hash");
        user.Role.ShouldBe(UserRole.Patient);
        user.IsEmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public void ReplacePendingRegistration_invalida_el_token_anterior_y_emite_uno_nuevo()
    {
        var user = UserMother.Pending();
        var later = Now.AddMinutes(5);

        user.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", later, Lifetime);

        user.Tokens.Count.ShouldBe(2);

        var previous = user.Tokens.Single(token => token.TokenHash == UserMother.DefaultTokenHash);
        previous.InvalidatedAt.ShouldBe(later);
        previous.IsValidAt(later).ShouldBeFalse();

        var current = user.Tokens.Single(token => token.TokenHash == "hash-2");
        current.ExpiresAt.ShouldBe(later + Lifetime);
        current.IsValidAt(later).ShouldBeTrue();
    }

    [Fact]
    public void ReplacePendingRegistration_funciona_aunque_el_token_anterior_haya_vencido()
    {
        var user = UserMother.Pending();
        var afterExpiration = Now + Lifetime + TimeSpan.FromMinutes(1);

        var result = user.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", afterExpiration, Lifetime);

        result.IsSuccess.ShouldBeTrue();
        user.Tokens.Count(token => token.IsValidAt(afterExpiration)).ShouldBe(1);
    }

    [Fact]
    public void ReplacePendingRegistration_dispara_el_evento_con_el_token_nuevo()
    {
        var user = UserMother.Pending();
        user.ClearDomainEvents();

        user.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", Now.AddMinutes(5), Lifetime);

        var domainEvent = user.DomainEvents.ShouldHaveSingleItem().ShouldBeOfType<UserTokenIssuedEvent>();
        domainEvent.RawToken.ShouldBe("raw-2");
    }

    [Fact]
    public void ReplacePendingRegistration_en_una_cuenta_confirmada_falla_con_correo_en_uso()
    {
        var user = UserMother.Confirmed();
        user.ClearDomainEvents();

        var result = user.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", Now.AddMinutes(5), Lifetime);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.EmailAlreadyInUse);
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
        user.Tokens.ShouldHaveSingleItem();
        user.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public void ReplacePendingRegistration_en_una_cuenta_desactivada_falla_sin_modificarla()
    {
        var user = UserMother.Pending();
        user.Deactivate();
        user.ClearDomainEvents();

        var result = user.ReplacePendingRegistration("nuevo-hash", "raw-2", "hash-2", Now.AddMinutes(5), Lifetime);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(UserErrors.AccountInactive);
        user.PasswordHash.ShouldBe(UserMother.DefaultPasswordHash);
        user.Tokens.ShouldHaveSingleItem();
        user.DomainEvents.ShouldBeEmpty();
    }
}
