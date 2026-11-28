using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Interceptors;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

/// <summary>
/// Aislamiento de los manejadores con el contexto real: lo que deja pendiente un manejador que falla se descarta, así
/// que el siguiente manejador que guarda no lo confirma por error.
/// </summary>
[Trait("Category", "Integration")]
public sealed class DomainEventDispatcherIsolationTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task Una_entidad_agregada_por_un_manejador_que_falla_no_la_guarda_el_siguiente()
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        var specialty = Specialty.Create($"Especialidad {Guid.NewGuid():N}", "Descripción de prueba.").Value;
        var name = specialty.Name;

        await Dispatcher(context, new FailsAfter(context, c => c.Add(specialty)), new Saves(context))
            .DispatchAsync([new SomethingHappened()]);

        await using var check = DoctorPersistenceData.Context(Database.ConnectionString);
        (await check.Specialties.AnyAsync(specialty => specialty.Name == name)).ShouldBeFalse();
    }

    [Fact]
    public async Task Un_cambio_hecho_por_un_manejador_que_falla_no_lo_guarda_el_siguiente()
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        var specialtyId = await TestDataSeeder.InsertSpecialtyAsync(context);
        var specialty = await context.Specialties.SingleAsync(value => value.Id == specialtyId);
        var original = specialty.Name;

        await Dispatcher(context, new FailsAfter(context, _ => specialty.Update(true, "Nombre cambiado", false, null)), new Saves(context))
            .DispatchAsync([new SomethingHappened()]);

        specialty.Name.ShouldBe(original);
        await using var check = DoctorPersistenceData.Context(Database.ConnectionString);
        (await check.Specialties.SingleAsync(value => value.Id == specialtyId)).Name.ShouldBe(original);
    }

    // El contexto se registra como en la aplicación: el despachador lo resuelve del mismo proveedor que los manejadores.
    private static DomainEventDispatcher Dispatcher(AppDbContext context, params IDomainEventHandler<SomethingHappened>[] handlers)
    {
        var services = new ServiceCollection();
        services.AddSingleton(context);
        foreach (var handler in handlers) services.AddSingleton(handler);

        return new DomainEventDispatcher(services.BuildServiceProvider(), new LoggerConfiguration().CreateLogger());
    }

    public sealed record SomethingHappened : IDomainEvent;

    private sealed class FailsAfter(AppDbContext context, Action<AppDbContext> change) : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken)
        {
            change(context);
            throw new InvalidOperationException("Falló después de cambiar el contexto");
        }
    }

    private sealed class Saves(AppDbContext context) : IDomainEventHandler<SomethingHappened>
    {
        public Task HandleAsync(SomethingHappened domainEvent, CancellationToken cancellationToken) =>
            context.SaveChangesAsync(cancellationToken);
    }
}
