using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using SGCM.Data.Application;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Notifications;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.Abstractions.Services;
using SGCM.Data.Application.Behaviors;
using SGCM.Data.Application.Identity;
using SGCM.Data.Application.Identity.Commands.RegisterPatientAccount;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Test.Common.Fakes;
using Shouldly;

namespace SGCM.Test.Unit.Application;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class ApplicationDependencyInjectionTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public void AddApplication_ResolvesCommandHandlerWrappedInValidation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterPatientAccountCommand, Unit>>();

        handler.ShouldBeOfType<ValidationDecorator<RegisterPatientAccountCommand, Unit>>();
    }

    [Fact]
    public async Task AddApplication_InvalidCommand_IsRejectedBeforeOpeningTransaction()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RegisterPatientAccountCommand, Unit>>();

        var result = await handler.HandleAsync(new RegisterPatientAccountCommand("no-es-un-correo", "Secreta123"), CancellationToken.None);

        result.Error.Code.ShouldBe(CommonErrors.InvalidEmail.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByEmailAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        services.AddSingleton(_userRepository);
        services.AddSingleton(_unitOfWork);
        services.AddSingleton(Substitute.For<IPasswordHasher>());
        services.AddSingleton(Substitute.For<ISecureTokenGenerator>());
        services.AddSingleton(Substitute.For<INotificationService>());
        services.AddSingleton<IDateTimeProvider>(new FakeDateTimeProvider());
        services.AddSingleton(Options.Create(new AccountsOptions { EmailConfirmationTokenMinutes = 15 }));
        services.AddSingleton(Substitute.For<IAttemptLimiter>());
        services.AddSingleton(Substitute.For<ITwoFactorStatusReader>());
        services.AddSingleton(Substitute.For<ISessionService>());
        services.AddSingleton(Options.Create(new LoginLimitsOptions()));
        services.AddSingleton(Options.Create(new PasswordResetLimitsOptions()));

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}