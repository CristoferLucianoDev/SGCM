using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Identity.Commands.CreateDoctorAccount;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using Serilog.Core;

namespace SGCM.Provisioning;

/// <summary>
/// Herramienta de administración para dar de alta cuentas de médico. No se despliega con
/// la web: la ejecuta un administrador con acceso a la cadena de conexión, que se lee de
/// los user-secrets de SGCM.Web en desarrollo o de ConnectionStrings__SgcmDatabase.
/// </summary>
internal static class Program
{
    private const string CreateDoctorVerb = "crear-medico";

    private const int Success = 0;
    private const int UsageError = 1;
    private const int CommandFailed = 2;
    private const int ConfigurationError = 3;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length != 2 || args[0] != CreateDoctorVerb)
        {
            PrintUsage();
            return UsageError;
        }

        await using var provider = BuildServiceProvider();

        try
        {
            // Valida la configuración antes de tocar la base, para dar un mensaje claro.
            _ = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        }
        catch (OptionsValidationException exception)
        {
            Console.Error.WriteLine("Configuración inválida:");
            foreach (var failure in exception.Failures)
            {
                Console.Error.WriteLine($"  {failure}");
            }

            return ConfigurationError;
        }

        return await CreateDoctorAsync(provider, args[1]);
    }

    private static ServiceProvider BuildServiceProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddUserSecrets(typeof(Program).Assembly, optional: true)
            .AddEnvironmentVariables()
            .Build();

        var services = new ServiceCollection();

        services.AddOptions<DatabaseOptions>().Bind(configuration.GetSection(DatabaseOptions.SectionName));
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidator>();

        // La salida de la herramienta es su resultado; los logs internos se descartan.
        services.AddSingleton<Serilog.ILogger>(Logger.None);

        services.AddApplication();
        services.AddInfrastructure();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<int> CreateDoctorAsync(ServiceProvider provider, string email)
    {
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider
            .GetRequiredService<ICommandHandler<CreateDoctorAccountCommand, CreateDoctorAccountResult>>();

        var result = await handler.HandleAsync(new CreateDoctorAccountCommand(email), CancellationToken.None);

        if (result.IsFailure)
        {
            Console.Error.WriteLine($"No se creó la cuenta: {result.Error.Description} ({result.Error.Code})");
            return CommandFailed;
        }

        Console.WriteLine($"Cuenta de médico creada: {result.Value.Email}");
        Console.WriteLine($"Contraseña temporal: {result.Value.TemporaryPassword}");
        Console.WriteLine("Entrégala al médico. No se volverá a mostrar.");

        return Success;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("Uso:");
        Console.Error.WriteLine($"  dotnet run --project SGCM.Provisioning -- {CreateDoctorVerb} <correo>");
    }
}