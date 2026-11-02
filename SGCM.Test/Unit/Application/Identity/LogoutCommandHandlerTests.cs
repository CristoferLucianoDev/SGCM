using NSubstitute;
using Shouldly;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Identity.Commands.Logout;

namespace SGCM.Test.Unit.Application.Identity;

/// <summary>
/// Pruebas del cierre de sesión en <see cref="LogoutCommandHandler"/>.
/// </summary>
public sealed class LogoutCommandHandlerTests
{
    private const string RefreshToken = "refresh-en-claro";

    private static readonly LogoutCommand Command = new(RefreshToken);

    private readonly ISessionService _sessionService = Substitute.For<ISessionService>();
    private readonly LogoutCommandHandler _handler;

    public LogoutCommandHandlerTests()
    {
        _handler = new LogoutCommandHandler(_sessionService);
    }

    [Fact]
    public async Task Logout_cierra_la_sesion_con_el_token_y_la_cancelacion_y_devuelve_exito()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await _handler.HandleAsync(Command, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        await _sessionService.Received(1).EndAsync(RefreshToken, cancellation.Token);
    }

    [Fact]
    public void ToString_del_comando_no_contiene_el_token()
    {
        Command.ToString().ShouldNotContain(RefreshToken);
    }
}
