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
using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Application.People.Commands.UpdateSpecialty;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class ApplicationDependencyInjectionTests
{
    private readonly IUserRepository _userRepository = Substitute.For<IUserRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ISpecialtyRepository _specialtyRepository = Substitute.For<ISpecialtyRepository>();
    private readonly ISpecialtyListReader _specialtyListReader = Substitute.For<ISpecialtyListReader>();
    private readonly IPatientRepository _patientRepository = Substitute.For<IPatientRepository>();

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task AddApplication_QueryValidatesAndReadsWithoutTransaction(int pageNumber, bool valid)
    {
        _specialtyListReader.ListAsync(null, true, false, pageNumber, 20, CancellationToken.None)
            .Returns(new PagedResult<SpecialtyDto>([], pageNumber, 20, 0));
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>>();

        handler.ShouldBeOfType<ListSpecialtiesQueryHandler>();
        scope.ServiceProvider.GetRequiredService<IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>>()
            .ShouldBeSameAs(handler);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetRequiredService<IQueryHandler<ListSpecialtiesQuery, PagedResult<SpecialtyDto>>>()
            .ShouldNotBeSameAs(handler);
        var result = await handler.HandleAsync(new ListSpecialtiesQuery(PageNumber: pageNumber), CancellationToken.None);

        result.IsSuccess.ShouldBe(valid);
        _specialtyListReader.ReceivedCalls().Count().ShouldBe(valid ? 1 : 0);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

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
        await _userRepository.DidNotReceive().GetByEmailForUpdateAsync(Arg.Any<Email>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddApplication_ResolvesCreateSpecialtyWithValidation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateSpecialtyCommand, int>>();

        handler.ShouldBeOfType<ValidationDecorator<CreateSpecialtyCommand, int>>();
    }

    [Theory]
    [InlineData("", "Descripción válida.", "Specialty.NameRequired")]
    [InlineData("Cardiología", "", "Specialty.DescriptionRequired")]
    public async Task AddApplication_InvalidSpecialty_IsRejectedBeforeOpeningTransaction(
        string name, string description, string errorCode)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateSpecialtyCommand, int>>();

        var result = await handler.HandleAsync(new CreateSpecialtyCommand(name, description), CancellationToken.None);

        result.Error.Code.ShouldBe(errorCode);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _specialtyRepository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _specialtyRepository.DidNotReceive().Add(Arg.Any<Specialty>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddApplication_CanceledSpecialty_IsRejectedBeforeOpeningTransaction()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CreateSpecialtyCommand, int>>();

        await Should.ThrowAsync<OperationCanceledException>(() => handler.HandleAsync(
            new CreateSpecialtyCommand("Cardiología", "Descripción válida."), cancellation.Token));

        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _specialtyRepository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _specialtyRepository.DidNotReceive().Add(Arg.Any<Specialty>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AddApplication_ResolvesCompletePatientProfileWithValidation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CompletePatientProfileCommand, SessionTokens>>();

        handler.ShouldBeOfType<ValidationDecorator<CompletePatientProfileCommand, SessionTokens>>();
    }

    [Fact]
    public async Task AddApplication_InvalidPatientProfile_IsRejectedBeforeOpeningTransaction()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<CompletePatientProfileCommand, SessionTokens>>();
        var command = CompletePatientProfileCommandMother.WithRequiredData() with { FirstName = "" };

        var result = await handler.HandleAsync(command, CancellationToken.None);

        result.Error.Code.ShouldBe(PatientErrors.FirstNameRequired.Code);
        await _unitOfWork.DidNotReceive().BeginTransactionAsync(Arg.Any<CancellationToken>());
        await _userRepository.DidNotReceive().GetByIdForUpdateAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        _patientRepository.DidNotReceive().Add(Arg.Any<Patient>());
    }

    [Fact]
    public void AddApplication_ResolvesUpdateSpecialtyWithValidationAndScopedLifetime()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>();

        handler.ShouldBeOfType<ValidationDecorator<UpdateSpecialtyCommand, Unit>>();
        scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>().ShouldBeSameAs(handler);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>().ShouldNotBeSameAs(handler);
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0, false, null, false, null, "Specialty.InvalidId")]
    [InlineData(42, false, null, false, null, "Specialty.NoFieldsProvided")]
    [InlineData(42, true, null, false, null, "Specialty.NameRequired")]
    [InlineData(42, false, null, true, null, "Specialty.DescriptionRequired")]
    public async Task AddApplication_InvalidUpdateIsRejectedBeforeOpeningTransaction(
        int id, bool nameProvided, string? name, bool descriptionProvided, string? description, string code)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>();

        var result = await handler.HandleAsync(new(id, nameProvided, name, descriptionProvided, description), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe(code);
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
        _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddApplication_UpdateSavesAndCommitsOnceUsingTheExistingTransaction(bool nameProvided)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = ConfigureUpdateSpecialty(cancellation.Token);
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>();

        var result = await handler.HandleAsync(new(42, nameProvided, "Neurología", true, "Descripción nueva."), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        specialty.Name.ShouldBe(nameProvided ? "Neurología" : "Cardiología");
        specialty.Description.ShouldBe("Descripción nueva.");
        specialty.Id.ShouldBe(42);
        specialty.IsActive.ShouldBeFalse();
        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(cancellation.Token);
            _specialtyRepository.GetByIdAsync(42, cancellation.Token);
            if (nameProvided)
            {
                _specialtyRepository.ExistsByNameAsync("Neurología", 42, cancellation.Token);
            }
            _unitOfWork.SaveChangesAsync(cancellation.Token);
            transaction.CommitAsync(cancellation.Token);
        });
        await _unitOfWork.Received(1).BeginTransactionAsync(cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await transaction.Received(1).CommitAsync(cancellation.Token);
        await transaction.Received(1).DisposeAsync();
        await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        _specialtyRepository.DidNotReceive().Add(Arg.Any<Specialty>());
        if (!nameProvided)
        {
            await _specialtyRepository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddApplication_MissingOrDuplicateUpdateRollsBackWithoutSaving(bool missing)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = missing ? null : ConfigureUpdateSpecialty(cancellation.Token);
        _specialtyRepository.ExistsByNameAsync("Neurología", 42, cancellation.Token).Returns(true);
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>();

        var result = await handler.HandleAsync(new(42, true, "Neurología", true, "Descripción nueva."), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(missing ? SpecialtyErrors.NotFound : SpecialtyErrors.NameAlreadyInUse);
        await _unitOfWork.Received(1).BeginTransactionAsync(cancellation.Token);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).RollbackAsync(cancellation.Token);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        if (specialty is not null)
        {
            specialty.Name.ShouldBe("Cardiología");
            specialty.Description.ShouldBe("Descripción original.");
            specialty.Id.ShouldBe(42);
            specialty.IsActive.ShouldBeFalse();
            specialty.DomainEvents.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task AddApplication_ConcurrentUpdateConflictRollsBackAndReturnsTheRecognizedError()
    {
        using var cancellation = new CancellationTokenSource();
        ConfigureUpdateSpecialty(cancellation.Token);
        var conflict = new PersistenceConflictException(SpecialtyErrors.NameAlreadyInUse, new InvalidOperationException());
        _unitOfWork.SaveChangesAsync(cancellation.Token).Returns(Task.FromException(conflict));
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>();

        var result = await handler.HandleAsync(new(42, true, "Neurología", false, null), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await transaction.Received(1).RollbackAsync(CancellationToken.None);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddApplication_CanceledUpdateDoesNotSaveOrCommit(bool beforeValidation)
    {
        using var cancellation = new CancellationTokenSource();
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        _specialtyRepository.GetByIdAsync(42, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Specialty?>(cancellation.Token);
        });
        if (beforeValidation)
        {
            cancellation.Cancel();
        }
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<UpdateSpecialtyCommand, Unit>>();

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => handler.HandleAsync(
            new(42, true, "Neurología", false, null), cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _unitOfWork.Received(beforeValidation ? 0 : 1).BeginTransactionAsync(cancellation.Token);
        await _specialtyRepository.Received(beforeValidation ? 0 : 1).GetByIdAsync(42, cancellation.Token);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(beforeValidation ? 0 : 1).DisposeAsync();
    }

    private Specialty ConfigureUpdateSpecialty(CancellationToken cancellationToken)
    {
        var specialty = Specialty.Create("Cardiología", "Descripción original.").Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, 42);
        specialty.Deactivate().IsSuccess.ShouldBeTrue();
        _specialtyRepository.GetByIdAsync(42, cancellationToken).Returns(specialty);
        return specialty;
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        services.AddSingleton(_userRepository);
        services.AddSingleton(_unitOfWork);
        services.AddSingleton(_specialtyRepository);
        services.AddSingleton(_specialtyListReader);
        services.AddSingleton(Substitute.For<ICurrentUser>());
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
        services.AddSingleton(Substitute.For<ITemporaryPasswordGenerator>());
        services.AddSingleton(_patientRepository);
        services.AddSingleton(Substitute.For<IInsuranceProviderReader>());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
