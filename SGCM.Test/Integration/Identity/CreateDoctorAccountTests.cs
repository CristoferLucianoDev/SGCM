using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Web;

namespace SGCM.Test.Integration.Identity;

public sealed class CreateDoctorAccountTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string DoctorEmail = "medico@correo.com";

    [Fact]
    public async Task El_medico_creado_inicia_sesion_y_debe_cambiar_la_contrasena()
    {
        var created = await CreateDoctorAsync(DoctorEmail);

        created.IsSuccess.ShouldBeTrue();
        var login = await LoginSuccessfullyAsync(DoctorEmail, created.Value.TemporaryPassword);
        login.PendingStep.ShouldBe("change_password");
    }

    [Fact]
    public async Task Un_correo_existente_se_rechaza_y_la_cuenta_original_no_cambia()
    {
        var first = await CreateDoctorAsync(DoctorEmail);

        var second = await CreateDoctorAsync(DoctorEmail);

        second.IsFailure.ShouldBeTrue();
        second.Error.ShouldBe(UserErrors.EmailAlreadyInUse);
        await LoginSuccessfullyAsync(DoctorEmail, first.Value.TemporaryPassword);
    }

    private async Task<Result<CreateDoctorAccountResult>> CreateDoctorAsync(string email)
    {
        using var scope = Factory.Services.CreateScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateDoctorAccountCommand, CreateDoctorAccountResult>>();

        return await handler.HandleAsync(new CreateDoctorAccountCommand(email), CancellationToken.None);
    }
}