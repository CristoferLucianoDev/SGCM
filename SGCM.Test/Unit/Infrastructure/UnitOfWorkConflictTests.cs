using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Patients;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Interceptors;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class UnitOfWorkConflictTests
{
    [Fact]
    public async Task SaveChangesAsync_traduce_solo_el_conflicto_de_especialidades_y_conserva_la_causa()
    {
        var postgres = CreatePostgresException("23505", "uq_specialty_name", "people", "specialty");
        var original = new DbUpdateException("Error de prueba.", postgres);

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(() => SaveThrowingAsync(original));

        conflict.Error.ShouldBe(SpecialtyErrors.NameAlreadyInUse);
        conflict.Error.Code.ShouldBe("Specialty.NameAlreadyInUse");
        conflict.Error.Type.ShouldBe(ErrorType.Conflict);
        conflict.InnerException.ShouldBeSameAs(original);
        conflict.InnerException!.InnerException.ShouldBeSameAs(postgres);
    }

    [Fact]
    public async Task SaveChangesAsync_traduce_el_perfil_de_paciente_duplicado_y_conserva_la_causa()
    {
        var postgres = CreatePostgresException("23505", "uq_patient_user_id", "people", "patient");
        var original = new DbUpdateException("Error de prueba.", postgres);

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(() => SaveThrowingAsync(original));

        conflict.Error.ShouldBe(PatientErrors.ProfileAlreadyExists);
        conflict.Error.Code.ShouldBe("Patient.ProfileAlreadyExists");
        conflict.Error.Type.ShouldBe(ErrorType.Conflict);
        conflict.InnerException.ShouldBeSameAs(original);
        conflict.InnerException!.InnerException.ShouldBeSameAs(postgres);
    }

    [Theory]
    [InlineData("23505", "pk_doctor_specialty", "app", "doctor_specialty")]
    [InlineData("23505", "pk_doctor_specialty", "people", "doctor")]
    [InlineData("23503", "pk_doctor_specialty", "people", "doctor_specialty")]
    [InlineData("23505", "uq_patient_user_id", "people", "specialty")]
    [InlineData("23505", "uq_specialty_name", "people", "patient")]
    [InlineData("23505", "uq_patient_user_id", "app", "patient")]
    [InlineData("23514", "uq_patient_user_id", "people", "patient")]
    [InlineData("23514", "ck_patient_policy_number", "people", "patient")]
    [InlineData("23505", "uq_user_email", "people", "specialty")]
    [InlineData("23505", "uq_specialty_name", "app", "specialty")]
    [InlineData("23505", "uq_specialty_name", "people", "user")]
    [InlineData("23503", "uq_specialty_name", "people", "specialty")]
    [InlineData("23514", "uq_specialty_name", "people", "specialty")]
    [InlineData("23505", null, "people", "specialty")]
    [InlineData("23505", "uq_specialty_name", null, "specialty")]
    [InlineData("23505", "uq_specialty_name", "people", null)]
    public async Task SaveChangesAsync_propaga_otros_errores_sin_traducir(
        string sqlState, string? constraint, string? schema, string? table)
    {
        var original = new DbUpdateException("Error de prueba.", CreatePostgresException(sqlState, constraint, schema, table));

        var actual = await Should.ThrowAsync<DbUpdateException>(() => SaveThrowingAsync(original));

        actual.ShouldBeSameAs(original);
    }

    [Fact]
    public async Task SaveChangesAsync_no_traduce_un_mensaje_que_menciona_el_indice()
    {
        var original = new DbUpdateException("uq_specialty_name 23505 people specialty", new InvalidOperationException());

        var actual = await Should.ThrowAsync<DbUpdateException>(() => SaveThrowingAsync(original));

        actual.ShouldBeSameAs(original);
    }

    [Fact]
    public async Task SaveChangesAsync_propaga_la_cancelacion()
    {
        var original = new OperationCanceledException();

        await Should.ThrowAsync<OperationCanceledException>(() => SaveThrowingAsync(original));
    }

    [Fact]
    public async Task SaveChangesAsync_traduce_la_clave_compuesta_del_vinculo_sin_perder_la_causa()
    {
        var postgres = CreatePostgresException("23505", "pk_doctor_specialty", "people", "doctor_specialty");
        var original = new DbUpdateException("Conflicto de vínculo.", postgres);

        var conflict = await Should.ThrowAsync<PersistenceConflictException>(() => SaveThrowingAsync(original));

        conflict.Error.ShouldBe(DoctorErrors.SpecialtyAlreadyAssociated);
        conflict.Error.Code.ShouldBe("Doctor.SpecialtyAlreadyAssociated");
        conflict.Error.Type.ShouldBe(ErrorType.Conflict);
        conflict.InnerException.ShouldBeSameAs(original);
        conflict.InnerException!.InnerException.ShouldBeSameAs(postgres);
    }

    private static PostgresException CreatePostgresException(string sqlState, string? constraint, string? schema, string? table) =>
        new("Error de prueba.", "ERROR", "ERROR", sqlState,
            schemaName: schema, tableName: table, constraintName: constraint);

    private static async Task SaveThrowingAsync(Exception exception)
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql()
            .AddInterceptors(new FailingSaveInterceptor(exception))
            .Options);
        var unitOfWork = new UnitOfWork(context, new DomainEventsQueue(),
            new DomainEventDispatcher(provider, Serilog.Core.Logger.None));

        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private sealed class FailingSaveInterceptor(Exception exception) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw exception;
    }
}
