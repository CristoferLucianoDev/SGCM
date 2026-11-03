using Microsoft.Extensions.DependencyInjection;
using FluentValidation;
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
using SGCM.Data.Application.Identity.Commands.ChangeTemporaryPassword;
using SGCM.Test.Unit.Domain.Identity.Users;
using SGCM.Data.Application.People.Commands.ActivateSpecialty;
using SGCM.Data.Application.People.Commands.AssociateDoctorSpecialty;
using SGCM.Data.Application.People.Commands.DisassociateDoctorSpecialty;
using SGCM.Data.Application.People.Commands.CompletePatientProfile;
using SGCM.Data.Application.People.Commands.CreateSpecialty;
using SGCM.Data.Application.People.Commands.DeactivateSpecialty;
using SGCM.Data.Application.People.Commands.UpdateSpecialty;
using SGCM.Data.Application.People.Queries.ListSpecialties;
using SGCM.Data.Application.Common;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Test.Common.Fakes;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Unit.Application.People;
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
    private readonly IDoctorRepository _doctorRepository = Substitute.For<IDoctorRepository>();

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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddApplication_ResolvesActivationHandlersAndValidatorsWithScopedLifetime(bool activate)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();

        if (activate)
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ActivateSpecialtyCommand, Unit>>();
            handler.ShouldBeOfType<ValidationDecorator<ActivateSpecialtyCommand, Unit>>();
            scope.ServiceProvider.GetRequiredService<ICommandHandler<ActivateSpecialtyCommand, Unit>>().ShouldBeSameAs(handler);
            otherScope.ServiceProvider.GetRequiredService<ICommandHandler<ActivateSpecialtyCommand, Unit>>().ShouldNotBeSameAs(handler);
            scope.ServiceProvider.GetRequiredService<ActivateSpecialtyCommandHandler>().ShouldNotBeNull();
            scope.ServiceProvider.GetServices<IValidator<ActivateSpecialtyCommand>>()
                .ShouldHaveSingleItem().ShouldBeOfType<ActivateSpecialtyCommandValidator>();
        }
        else
        {
            var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<DeactivateSpecialtyCommand, Unit>>();
            handler.ShouldBeOfType<ValidationDecorator<DeactivateSpecialtyCommand, Unit>>();
            scope.ServiceProvider.GetRequiredService<ICommandHandler<DeactivateSpecialtyCommand, Unit>>().ShouldBeSameAs(handler);
            otherScope.ServiceProvider.GetRequiredService<ICommandHandler<DeactivateSpecialtyCommand, Unit>>().ShouldNotBeSameAs(handler);
            scope.ServiceProvider.GetRequiredService<DeactivateSpecialtyCommandHandler>().ShouldNotBeNull();
            scope.ServiceProvider.GetServices<IValidator<DeactivateSpecialtyCommand>>()
                .ShouldHaveSingleItem().ShouldBeOfType<DeactivateSpecialtyCommandValidator>();
        }
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
        _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    public async Task AddApplication_InvalidActivationIsRejectedBeforeOpeningTransaction(bool activate, int id)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await HandleActivation(scope.ServiceProvider, activate, id, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(SpecialtyErrors.InvalidId);
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
        _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddApplication_ActivationSavesAndCommitsOnceAfterTheHandler(bool activate)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = ConfigureActivationSpecialty(!activate, cancellation.Token);
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        _unitOfWork.SaveChangesAsync(cancellation.Token).Returns(_ =>
        {
            specialty.IsActive.ShouldBe(activate);
            return Task.CompletedTask;
        });
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await HandleActivation(scope.ServiceProvider, activate, 42, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        specialty.Id.ShouldBe(42);
        specialty.Name.ShouldBe("Cardiología");
        specialty.Description.ShouldBe("Descripción original.");
        specialty.IsActive.ShouldBe(activate);
        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(cancellation.Token);
            _specialtyRepository.GetByIdAsync(42, cancellation.Token);
            _unitOfWork.SaveChangesAsync(cancellation.Token);
            transaction.CommitAsync(cancellation.Token);
        });
        await _unitOfWork.Received(1).BeginTransactionAsync(cancellation.Token);
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await transaction.Received(1).CommitAsync(cancellation.Token);
        await transaction.Received(1).DisposeAsync();
        await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        _specialtyRepository.ReceivedCalls().ShouldHaveSingleItem();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AddApplication_MissingOrConflictingActivationRollsBackWithoutSaving(bool activate, bool missing)
    {
        using var cancellation = new CancellationTokenSource();
        var specialty = missing ? null : ConfigureActivationSpecialty(activate, cancellation.Token);
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await HandleActivation(scope.ServiceProvider, activate, 42, cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(missing ? SpecialtyErrors.NotFound : activate
            ? ActivationErrors.AlreadyActive(nameof(Specialty))
            : ActivationErrors.AlreadyInactive(nameof(Specialty)));
        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(cancellation.Token);
            _specialtyRepository.GetByIdAsync(42, cancellation.Token);
            transaction.RollbackAsync(cancellation.Token);
        });
        await _unitOfWork.Received(1).BeginTransactionAsync(cancellation.Token);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).RollbackAsync(cancellation.Token);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        _specialtyRepository.ReceivedCalls().ShouldHaveSingleItem();
        if (specialty is not null)
        {
            specialty.Id.ShouldBe(42);
            specialty.Name.ShouldBe("Cardiología");
            specialty.Description.ShouldBe("Descripción original.");
            specialty.IsActive.ShouldBe(activate);
            specialty.DomainEvents.ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AddApplication_CanceledActivationDoesNotSaveOrCommit(bool activate, bool beforeValidation)
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

        var exception = await Should.ThrowAsync<OperationCanceledException>(() =>
            HandleActivation(scope.ServiceProvider, activate, 42, cancellation.Token));

        exception.CancellationToken.ShouldBe(cancellation.Token);
        await _unitOfWork.Received(beforeValidation ? 0 : 1).BeginTransactionAsync(cancellation.Token);
        await _specialtyRepository.Received(beforeValidation ? 0 : 1).GetByIdAsync(42, cancellation.Token);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(beforeValidation ? 0 : 1).DisposeAsync();
    }

    private static Task<Result<Unit>> HandleActivation(IServiceProvider provider, bool activate, int id, CancellationToken cancellationToken) =>
        activate
            ? provider.GetRequiredService<ICommandHandler<ActivateSpecialtyCommand, Unit>>()
                .HandleAsync(new(id), cancellationToken)
            : provider.GetRequiredService<ICommandHandler<DeactivateSpecialtyCommand, Unit>>()
                .HandleAsync(new(id), cancellationToken);

    private Specialty ConfigureActivationSpecialty(bool active, CancellationToken cancellationToken)
    {
        var specialty = Specialty.Create("Cardiología", "Descripción original.").Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, 42);
        if (!active)
        {
            specialty.Deactivate().IsSuccess.ShouldBeTrue();
        }
        _specialtyRepository.GetByIdAsync(42, cancellationToken).Returns(specialty);
        return specialty;
    }

    private Specialty ConfigureUpdateSpecialty(CancellationToken cancellationToken)
    {
        var specialty = Specialty.Create("Cardiología", "Descripción original.").Value;
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(specialty, 42);
        specialty.Deactivate().IsSuccess.ShouldBeTrue();
        _specialtyRepository.GetByIdAsync(42, cancellationToken).Returns(specialty);
        return specialty;
    }


    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AddApplication_resuelve_comandos_Doctor_y_validadores_automaticamente_por_scope(bool associate)
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        if (associate)
        {
            var handler = first.ServiceProvider.GetRequiredService<ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>>();
            handler.ShouldBeOfType<ValidationDecorator<AssociateDoctorSpecialtyCommand, Unit>>();
            handler.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>>());
            handler.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>>());
            first.ServiceProvider.GetRequiredService<AssociateDoctorSpecialtyCommandHandler>().ShouldNotBeNull();
            first.ServiceProvider.GetServices<IValidator<AssociateDoctorSpecialtyCommand>>()
                .ShouldHaveSingleItem().ShouldBeOfType<AssociateDoctorSpecialtyCommandValidator>();
        }
        else
        {
            var handler = first.ServiceProvider.GetRequiredService<ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>>();
            handler.ShouldBeOfType<ValidationDecorator<DisassociateDoctorSpecialtyCommand, Unit>>();
            handler.ShouldBeSameAs(first.ServiceProvider.GetRequiredService<ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>>());
            handler.ShouldNotBeSameAs(second.ServiceProvider.GetRequiredService<ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>>());
            first.ServiceProvider.GetRequiredService<DisassociateDoctorSpecialtyCommandHandler>().ShouldNotBeNull();
            first.ServiceProvider.GetServices<IValidator<DisassociateDoctorSpecialtyCommand>>()
                .ShouldHaveSingleItem().ShouldBeOfType<DisassociateDoctorSpecialtyCommandValidator>();
        }
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
        _doctorRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(true, -1)]
    [InlineData(true, int.MinValue)]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(false, int.MinValue)]
    public async Task AddApplication_Doctor_invalido_no_abre_transaccion_ni_lee_perfiles(bool associate, int id)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var result = await HandleDoctorSpecialty(scope.ServiceProvider, associate, id, CancellationToken.None);

        result.Error.ShouldBe(SpecialtyErrors.InvalidId);
        result.Error.Code.ShouldBe("Specialty.InvalidId");
        result.Error.Type.ShouldBe(ErrorType.Validation);
        _unitOfWork.ReceivedCalls().ShouldBeEmpty();
        _doctorRepository.ReceivedCalls().ShouldBeEmpty();
        _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddApplication_Doctor_guarda_y_confirma_una_vez_despues_de_la_operacion(bool associate)
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Returns(42);
        var doctor = DoctorCommandData.Doctor(11, 22);
        var expected = associate ? new[] { 11, 22, 33 } : new[] { 22 };
        _doctorRepository.GetByUserIdForUpdateAsync(42, cancellation.Token).Returns(doctor);
        _specialtyRepository.GetByIdAsync(33, cancellation.Token).Returns(DoctorCommandData.Specialty(33));
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        _unitOfWork.SaveChangesAsync(cancellation.Token).Returns(_ =>
        {
            doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(expected, ignoreOrder: true);
            return Task.CompletedTask;
        });

        var result = await HandleDoctorSpecialty(scope.ServiceProvider, associate, associate ? 33 : 11, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        Received.InOrder(() =>
        {
            _unitOfWork.BeginTransactionAsync(cancellation.Token);
            _doctorRepository.GetByUserIdForUpdateAsync(42, cancellation.Token);
            if (associate) _specialtyRepository.GetByIdAsync(33, cancellation.Token);
            _unitOfWork.SaveChangesAsync(cancellation.Token);
            transaction.CommitAsync(cancellation.Token);
        });
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
        await transaction.Received(1).CommitAsync(cancellation.Token);
        await transaction.DidNotReceive().RollbackAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        if (!associate) _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, "missingProfile", "Doctor.NotFound", ErrorType.NotFound)]
    [InlineData(false, "missingProfile", "Doctor.NotFound", ErrorType.NotFound)]
    [InlineData(true, "legacy", "Doctor.ProfileIncomplete", ErrorType.Conflict)]
    [InlineData(false, "legacy", "Doctor.ProfileIncomplete", ErrorType.Conflict)]
    [InlineData(true, "missingSpecialty", "Specialty.NotFound", ErrorType.NotFound)]
    [InlineData(true, "inactive", "Specialty.Inactive", ErrorType.Conflict)]
    [InlineData(true, "duplicate", "Doctor.SpecialtyAlreadyAssociated", ErrorType.Conflict)]
    [InlineData(false, "last", "Doctor.CannotRemoveLastSpecialty", ErrorType.Conflict)]
    [InlineData(false, "missingLink", "Doctor.SpecialtyNotAssociated", ErrorType.Conflict)]
    public async Task AddApplication_Doctor_revierte_errores_sin_guardar_ni_mutar(
        bool associate, string scenario, string code, ErrorType type)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Returns(42);
        var doctor = scenario == "legacy" ? DoctorCommandData.Legacy()
            : DoctorCommandData.Doctor(scenario == "last" ? [11] : [11, 22]);
        var profile = DoctorCommandData.Profile(doctor);
        var links = doctor.Specialties.ToArray();
        if (scenario != "missingProfile")
            _doctorRepository.GetByUserIdForUpdateAsync(42, CancellationToken.None).Returns(doctor);
        var id = scenario == "duplicate" ? 11 : scenario == "missingLink" ? 99 : associate ? 33 : 11;
        if (associate && scenario != "missingSpecialty")
            _specialtyRepository.GetByIdAsync(id, CancellationToken.None)
                .Returns(DoctorCommandData.Specialty(id, scenario != "inactive"));
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(CancellationToken.None).Returns(transaction);

        var result = await HandleDoctorSpecialty(scope.ServiceProvider, associate, id, CancellationToken.None);

        result.Error.Code.ShouldBe(code);
        result.Error.Type.ShouldBe(type);
        DoctorCommandData.Profile(doctor).ShouldBe(profile);
        doctor.Specialties.ShouldBe(links);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).RollbackAsync(CancellationToken.None);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
        if (!associate || scenario is "missingProfile" or "legacy") _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task AddApplication_Doctor_cancelado_no_guarda_ni_confirma(bool associate, bool beforeValidation)
    {
        using var cancellation = new CancellationTokenSource();
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Returns(42);
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(cancellation.Token).Returns(transaction);
        _doctorRepository.GetByUserIdForUpdateAsync(42, cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<Doctor?>(cancellation.Token);
        });
        if (beforeValidation) cancellation.Cancel();

        var error = await Should.ThrowAsync<OperationCanceledException>(() =>
            HandleDoctorSpecialty(scope.ServiceProvider, associate, 11, cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
        await _unitOfWork.Received(beforeValidation ? 0 : 1).BeginTransactionAsync(cancellation.Token);
        await _doctorRepository.Received(beforeValidation ? 0 : 1).GetByUserIdForUpdateAsync(42, cancellation.Token);
        _specialtyRepository.ReceivedCalls().ShouldBeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(beforeValidation ? 0 : 1).DisposeAsync();
    }

    [Fact]
    public async Task AddApplication_Doctor_conflicto_de_persistencia_se_devuelve_tras_rollback()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>().UserId.Returns(42);
        _doctorRepository.GetByUserIdForUpdateAsync(42, CancellationToken.None).Returns(DoctorCommandData.Doctor(11));
        _specialtyRepository.GetByIdAsync(22, CancellationToken.None).Returns(DoctorCommandData.Specialty(22));
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(CancellationToken.None).Returns(transaction);
        _unitOfWork.SaveChangesAsync(CancellationToken.None).Returns(Task.FromException(
            new PersistenceConflictException(DoctorErrors.SpecialtyAlreadyAssociated, new InvalidOperationException())));

        var result = await HandleDoctorSpecialty(scope.ServiceProvider, true, 22, CancellationToken.None);

        result.Error.ShouldBe(DoctorErrors.SpecialtyAlreadyAssociated);
        result.Error.Code.ShouldBe("Doctor.SpecialtyAlreadyAssociated");
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        await transaction.Received(1).RollbackAsync(CancellationToken.None);
        await transaction.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        await transaction.Received(1).DisposeAsync();
    }

    private static Task<Result<Unit>> HandleDoctorSpecialty(
        IServiceProvider provider, bool associate, int id, CancellationToken token) => associate
        ? provider.GetRequiredService<ICommandHandler<AssociateDoctorSpecialtyCommand, Unit>>().HandleAsync(new(id), token)
        : provider.GetRequiredService<ICommandHandler<DisassociateDoctorSpecialtyCommand, Unit>>().HandleAsync(new(id), token);

    [Theory]
    [InlineData("success")]
    [InlineData("validation")]
    [InlineData("unchanged")]
    [InlineData("renewal")]
    [InlineData("save")]
    public async Task Cambio_temporal_se_resuelve_con_validacion_y_transaccion(string kind)
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var data = new PasswordSessionScenario();
        scope.ServiceProvider.GetRequiredService<ICurrentUser>()
            .TryGetSessionIdentity(out Arg.Any<int>(), out Arg.Any<int>()).Returns(call =>
            { call[0] = data.Owner.Id; call[1] = data.Current.Id; return true; });
        _userRepository.GetByIdForUpdateAsync(data.Owner.Id, CancellationToken.None).Returns(data.Owner);
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        hasher.Verify(Arg.Any<string>(), Arg.Any<string>()).Returns(kind == "unchanged");
        hasher.Hash(Arg.Any<Password>()).Returns("nuevo-hash");
        var sessionService = scope.ServiceProvider.GetRequiredService<ISessionService>();
        var tokens = new SessionTokens(new("jwt", UserMother.Now.AddMinutes(15)), "refresh", UserMother.Now.AddDays(7), PendingStep.DoctorProfile);
        sessionService.RenewCurrentAsync(data.Owner, data.Current.Id, UserMother.Now, CancellationToken.None)
            .Returns(kind == "renewal" ? Result<SessionTokens>.Failure(UserErrors.SessionInvalid) : tokens);
        var transaction = Substitute.For<ITransaction>();
        _unitOfWork.BeginTransactionAsync(CancellationToken.None).Returns(transaction);
        if (kind == "save") _unitOfWork.SaveChangesAsync(CancellationToken.None)
            .Returns(Task.FromException(new InvalidOperationException("Fallo de persistencia")));
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ChangeTemporaryPasswordCommand, SessionTokens>>();
        if (kind == "save")
            await Should.ThrowAsync<InvalidOperationException>(() => handler.HandleAsync(new("NuevaClave123"), CancellationToken.None));
        else
        {
            var result = await handler.HandleAsync(new(kind == "validation" ? "corta" : "NuevaClave123"), CancellationToken.None);
            if (kind == "success") result.Value.ShouldBe(tokens);
            else result.Error.ShouldBe(kind == "validation" ? UserErrors.PasswordTooShort
                : kind == "unchanged" ? UserErrors.PasswordUnchanged : UserErrors.SessionInvalid);
        }
        await transaction.Received(kind == "success" ? 1 : 0).CommitAsync(CancellationToken.None);
        await transaction.Received(kind is "unchanged" or "renewal" ? 1 : 0).RollbackAsync(CancellationToken.None);
        await transaction.Received(kind == "validation" ? 0 : 1).DisposeAsync();
        await _unitOfWork.Received(kind is "success" or "save" ? 1 : 0).SaveChangesAsync(CancellationToken.None);
        if (kind == "validation") _userRepository.ReceivedCalls().ShouldBeEmpty();
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
        services.AddSingleton(_doctorRepository);
        services.AddSingleton(Substitute.For<IInsuranceProviderReader>());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
