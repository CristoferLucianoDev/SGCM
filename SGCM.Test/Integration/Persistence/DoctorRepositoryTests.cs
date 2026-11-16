using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Repositories.People;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class DoctorRepositoryTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GetByUserId_carga_todo_con_tracking_incluyendo_vinculos_inactivos(bool active)
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext, active: active);
        seed.UserId.ShouldNotBe(seed.DoctorId);
        await using var context = Context();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        await using var transaction = await context.Database.BeginTransactionAsync();

        var doctor = await new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        doctor.ShouldNotBeNull();
        doctor.Id.ShouldBe(seed.DoctorId);
        doctor.UserId.ShouldBe(seed.UserId);
        doctor.IsActive.ShouldBe(active);
        doctor.FirstName.ShouldBe("Luis");
        doctor.LastName.ShouldBe("Pérez");
        doctor.LicenseNumber.ShouldStartWith("TASK226-");
        doctor.PhoneNumber.ShouldBe(PhoneNumber.Create("8095550200").Value);
        doctor.ProvinceId.ShouldBeGreaterThan(0);
        doctor.MunicipalityId.ShouldBeGreaterThan(0);
        doctor.AddressLine.ShouldBe("Calle de pruebas 226");
        doctor.ConsultationFee.ShouldBe(Money.Create(1500m).Value);
        doctor.YearsOfExperience.ShouldBe(10);
        doctor.Biography.ShouldBe("Perfil médico de prueba.");
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(seed.SpecialtyIds.Take(2), ignoreOrder: true);
        context.Entry(doctor).State.ShouldBe(EntityState.Unchanged);
        context.ChangeTracker.Entries<DoctorSpecialty>().Count().ShouldBe(2);
        context.ChangeTracker.Entries<DoctorSpecialty>().ShouldAllBe(entry => entry.State == EntityState.Unchanged);
        doctor.Specialties.ShouldBeAssignableTo<ICollection<DoctorSpecialty>>().IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public async Task GetByUserId_no_encuentra_otro_perfil_y_materializa_legados_sin_inventar_vinculos()
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context, associations: 0);
        await using var transaction = await context.Database.BeginTransactionAsync();
        var repository = new DoctorRepository(context);

        (await repository.GetByUserIdForUpdateAsync(int.MaxValue, CancellationToken.None)).ShouldBeNull();
        var legacy = await repository.GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        legacy.ShouldNotBeNull();
        legacy.Specialties.ShouldBeEmpty();
        context.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" \t  ")]
    [InlineData(" 8095550200 ")]
    public async Task GetByUserId_conserva_el_telefono_legado_al_guardar_un_vinculo(string storedPhone)
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context);
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET phone_number = {storedPhone} WHERE id = {seed.DoctorId}");
        await using var transaction = await context.Database.BeginTransactionAsync();
        var before = await ReadProfileRowAsync(context, seed.DoctorId);
        var doctor = await new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        doctor.ShouldNotBeNull();
        doctor.PhoneNumber.Value.ShouldBe(storedPhone);
        doctor.AssociateSpecialty(seed.SpecialtyIds[2]).IsSuccess.ShouldBeTrue();

        context.Entry(doctor).State.ShouldBe(EntityState.Unchanged);
        await context.SaveChangesAsync();
        (await ReadProfileRowAsync(context, seed.DoctorId)).ShouldBe(before);
        context.ChangeTracker.Clear();
        var loaded = await context.Doctors.Include(value => value.Specialties).SingleAsync(value => value.Id == seed.DoctorId);
        loaded.PhoneNumber.Value.ShouldBe(storedPhone);
        loaded.Specialties.Select(link => link.SpecialtyId).ShouldBe(seed.SpecialtyIds, ignoreOrder: true);
    }

    [Fact]
    public async Task GetByUserId_exige_transaccion_y_propaga_cancelacion()
    {
        await using var context = Context();
        var repository = new DoctorRepository(context);
        await Should.ThrowAsync<InvalidOperationException>(() =>
            repository.GetByUserIdForUpdateAsync(1, CancellationToken.None));
        await using var transaction = await context.Database.BeginTransactionAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Should.ThrowAsync<OperationCanceledException>(() =>
            repository.GetByUserIdForUpdateAsync(1, cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
        context.ChangeTracker.HasChanges().ShouldBeFalse();
    }

    [Fact]
    public async Task GetByUserId_recarga_vinculos_obsoletos_sin_descartar_cambios_de_otros_agregados()
    {
        await using var seedContext = Context();
        var seed = await DoctorPersistenceData.SeedAsync(seedContext);
        await using var context = Context();
        var stale = await context.Doctors.Include(doctor => doctor.Specialties).SingleAsync();
        await using (var other = Context())
            await other.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM people.doctor_specialty WHERE doctor_id = {seed.DoctorId} AND specialty_id = {seed.SpecialtyIds[0]}");
        var unrelated = Specialty.Create("Especialidad pendiente", "No debe descartarse.").Value;
        context.Specialties.Add(unrelated);
        await using var transaction = await context.Database.BeginTransactionAsync();

        var fresh = await new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None);

        fresh.ShouldNotBeNull();
        fresh.ShouldNotBeSameAs(stale);
        fresh.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(seed.SpecialtyIds[1]);
        context.Entry(unrelated).State.ShouldBe(EntityState.Added);
        context.ChangeTracker.Entries<DoctorSpecialty>().ShouldHaveSingleItem().State.ShouldBe(EntityState.Unchanged);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("associate")]
    [InlineData("disassociate")]
    public async Task GetByUserId_no_descarta_cambios_pendientes_del_grafo(string operation)
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context);
        var doctor = await context.Doctors.SingleAsync();
        if (operation == "profile") doctor.Deactivate().IsSuccess.ShouldBeTrue();
        else if (operation == "associate") doctor.AssociateSpecialty(seed.SpecialtyIds[2]).IsSuccess.ShouldBeTrue();
        else doctor.DisassociateSpecialty(seed.SpecialtyIds[0]).IsSuccess.ShouldBeTrue();
        var links = doctor.Specialties.ToArray();
        await using var transaction = await context.Database.BeginTransactionAsync();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            new DoctorRepository(context).GetByUserIdForUpdateAsync(seed.UserId, CancellationToken.None));

        context.ChangeTracker.HasChanges().ShouldBeTrue();
        context.Entry(doctor).State.ShouldBe(operation == "profile" ? EntityState.Modified : EntityState.Unchanged);
        var expected = operation == "profile" ? seed.SpecialtyIds.Take(2)
            : operation == "associate" ? seed.SpecialtyIds : [seed.SpecialtyIds[1]];
        doctor.Specialties.Select(link => link.SpecialtyId).ShouldBe(expected, ignoreOrder: true);
        doctor.Specialties.ShouldBe(links);
        if (operation == "disassociate")
            context.ChangeTracker.Entries<DoctorSpecialty>().Single(entry => entry.State == EntityState.Deleted)
                .Entity.SpecialtyId.ShouldBe(seed.SpecialtyIds[0]);
    }

    [Fact]
    public async Task Add_persiste_identidad_y_vinculos_inmutables_desde_la_fabrica()
    {
        await using var context = Context();
        var seed = await DoctorPersistenceData.SeedAsync(context);
        var source = await context.Doctors.AsNoTracking().SingleAsync();
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"new-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var doctor = DoctorMother.Create([seed.SpecialtyIds[0]], userId: user.Id,
            provinceId: source.ProvinceId, municipalityId: source.MunicipalityId,
            licenseNumber: $"LIC-{Guid.NewGuid():N}", phoneNumber: " 809-555-0100 ",
            consultationFee: 99999999.990m).Value;
        doctor.Deactivate().IsSuccess.ShouldBeTrue();
        var repository = new DoctorRepository(context);

        repository.Add(doctor);
        doctor.Id.ShouldBe(0);
        context.Entry(doctor).State.ShouldBe(EntityState.Added);
        await context.SaveChangesAsync();
        doctor.Id.ShouldBeGreaterThan(0);
        context.ChangeTracker.Clear();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var loaded = await repository.GetByUserIdForUpdateAsync(user.Id, CancellationToken.None);

        loaded.ShouldNotBeNull();
        loaded.Id.ShouldBe(doctor.Id);
        loaded.UserId.ShouldBe(user.Id);
        loaded.IsActive.ShouldBeFalse();
        loaded.FirstName.ShouldBe(DoctorMother.FirstName);
        loaded.ProvinceId.ShouldBe(source.ProvinceId);
        loaded.MunicipalityId.ShouldBe(source.MunicipalityId);
        loaded.PhoneNumber.ShouldBe(doctor.PhoneNumber);
        loaded.ConsultationFee.ShouldBe(doctor.ConsultationFee);
        (await context.Database.SqlQuery<string>(
            $"""SELECT phone_number AS "Value" FROM people.doctor WHERE id = {doctor.Id}""").SingleAsync())
            .ShouldBe("809-555-0100");
        (await context.Database.SqlQuery<string>(
            $"""SELECT consultation_fee::text AS "Value" FROM people.doctor WHERE id = {doctor.Id}""").SingleAsync())
            .ShouldBe("99999999.99");
        loaded.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(seed.SpecialtyIds[0]);
        (await context.Doctors.AsNoTracking().SingleAsync(value => value.Id == seed.DoctorId))
            .Specialties.Select(link => link.SpecialtyId).ShouldBe(seed.SpecialtyIds.Take(2), ignoreOrder: true);
    }

    [Theory]
    [InlineData("provincia_incorrecta")]
    [InlineData("municipio_inexistente")]
    [InlineData("provincia_inexistente")]
    public async Task Add_rechaza_ubicaciones_incompatibles_en_PostgreSQL(string scenario)
    {
        var seed = await SeedRegistrationAsync();
        await using var context = Context();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var location = scenario switch
        {
            "provincia_incorrecta" => seed with
            {
                ProvinceId = await context.Database.SqlQuery<int>(
                    $"""SELECT id AS "Value" FROM people.province WHERE id <> {seed.ProvinceId} ORDER BY id LIMIT 1""")
                    .SingleAsync()
            },
            "municipio_inexistente" => seed with { MunicipalityId = int.MaxValue },
            "provincia_inexistente" => seed with { ProvinceId = int.MaxValue },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };

        if (scenario == "provincia_inexistente")
        {
            (await context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM people.province WHERE id = {location.ProvinceId}) AS \"Value\"")
                .SingleAsync()).ShouldBeFalse("La provincia de prueba debe estar ausente del catálogo.");
        }
        else if (scenario == "municipio_inexistente")
        {
            (await context.Database.SqlQuery<bool>(
                $"SELECT EXISTS (SELECT 1 FROM people.municipality WHERE id = {location.MunicipalityId}) AS \"Value\"")
                .SingleAsync()).ShouldBeFalse("El municipio de prueba debe estar ausente del catálogo.");
        }

        new DoctorRepository(context).Add(location.CreateDoctor(0, "LIC-251-GEO"));

        var failure = await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());

        var postgres = AssertPostgresFailure(failure, PostgresErrorCodes.ForeignKeyViolation);
        if (scenario == "provincia_inexistente")
        {
            // Una provincia ausente también invalida la pareja de la FK compuesta.
            string[] geographicKeys = ["fk_doctor_province", "fk_doctor_municipality"];
            geographicKeys.ShouldContain(postgres.ConstraintName);
        }
        else
        {
            postgres.ConstraintName.ShouldBe("fk_doctor_municipality");
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveChanges_distingue_licencia_duplicada_de_usuario_duplicado(bool duplicateLicense)
    {
        var seed = await SeedRegistrationAsync(2);
        await using var provider = DoctorPersistenceData.Provider(Database.ConnectionString);
        await using var scope = provider.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDoctorRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using var transaction = await unitOfWork.BeginTransactionAsync(CancellationToken.None);
        const string license = "LIC-251-DUP";
        repository.Add(seed.CreateDoctor(0, license));
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
        repository.Add(duplicateLicense ? seed.CreateDoctor(1, license) : seed.CreateDoctor(0, "LIC-251-OTRA"));

        var failure = await Record.ExceptionAsync(() => unitOfWork.SaveChangesAsync(CancellationToken.None));
        var conflict = failure.ShouldBeOfType<PersistenceConflictException>();

        if (duplicateLicense)
        {
            AssertLicenseConflict(conflict);
        }
        else
        {
            conflict.Error.ShouldBe(DoctorErrors.ProfileAlreadyExists);
            var postgres = AssertPostgresFailure(conflict.InnerException.ShouldBeOfType<DbUpdateException>(),
                PostgresErrorCodes.UniqueViolation);
            postgres.ConstraintName.ShouldBe("uq_doctor_user_id");
        }

        await transaction.RollbackAsync(CancellationToken.None);
        await using var verification = Context();
        (await verification.Doctors.AnyAsync(doctor => seed.UserIds.Contains(doctor.UserId))).ShouldBeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExistsByLicenseNumber_compara_el_valor_persistido_sin_cargar_perfiles(bool active)
    {
        var seed = await SeedRegistrationAsync();
        const string license = "LIC-251-Exacta";
        await using (var setup = Context())
        {
            var doctor = seed.CreateDoctor(0, $" {license} ");
            doctor.LicenseNumber.ShouldBe(license);
            if (!active) doctor.Deactivate().IsSuccess.ShouldBeTrue();
            new DoctorRepository(setup).Add(doctor);
            await setup.SaveChangesAsync();
        }

        await using var context = Context();
        IDoctorRepository repository = new DoctorRepository(context);

        (await repository.ExistsByLicenseNumberAsync(license, CancellationToken.None)).ShouldBeTrue();
        (await repository.ExistsByLicenseNumberAsync("LIC-251-Disponible", CancellationToken.None)).ShouldBeFalse();
        (await repository.ExistsByLicenseNumberAsync("lic-251-exacta", CancellationToken.None)).ShouldBeFalse();
        (await repository.ExistsByLicenseNumberAsync($"{license} ", CancellationToken.None)).ShouldBeFalse();
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task ExistsByLicenseNumber_propaga_la_cancelacion()
    {
        await using var context = Context();
        IDoctorRepository repository = new DoctorRepository(context);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var error = await Should.ThrowAsync<OperationCanceledException>(() =>
            repository.ExistsByLicenseNumberAsync("LIC-251", cancellation.Token));

        error.CancellationToken.ShouldBe(cancellation.Token);
        context.ChangeTracker.Entries().ShouldBeEmpty();
    }

    [Fact]
    public async Task Dos_altas_con_la_misma_licencia_solo_confirman_un_perfil_y_sus_vinculos()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var seed = await SeedRegistrationAsync(2, timeout.Token);
        await using var provider = DoctorPersistenceData.Provider(Database.ConnectionString);
        await using var firstScope = provider.CreateAsyncScope();
        await using var secondScope = provider.CreateAsyncScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstRepository = firstScope.ServiceProvider.GetRequiredService<IDoctorRepository>();
        var secondRepository = secondScope.ServiceProvider.GetRequiredService<IDoctorRepository>();
        var firstUnit = firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var secondUnit = secondScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await using var firstTransaction = await firstUnit.BeginTransactionAsync(timeout.Token);
        await using var secondTransaction = await secondUnit.BeginTransactionAsync(timeout.Token);
        const string license = "LIC-251-Concurrente";

        (await firstRepository.ExistsByLicenseNumberAsync(license, timeout.Token)).ShouldBeFalse();
        (await secondRepository.ExistsByLicenseNumberAsync(license, timeout.Token)).ShouldBeFalse();
        var losingSpecialty = Specialty.Create("Especialidad del alta rechazada", "Verifica el rollback.").Value;
        secondContext.Specialties.Add(losingSpecialty);
        await secondUnit.SaveChangesAsync(timeout.Token);
        firstRepository.Add(seed.CreateDoctor(0, license));
        secondRepository.Add(seed.CreateDoctor(1, license, losingSpecialty.Id));
        await firstUnit.SaveChangesAsync(timeout.Token);

        var firstPid = await firstContext.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(timeout.Token);
        var secondPid = await secondContext.Database.SqlQuery<int>($"SELECT pg_backend_pid() AS \"Value\"")
            .SingleAsync(timeout.Token);
        var pendingSave = Record.ExceptionAsync(() => secondUnit.SaveChangesAsync(timeout.Token));
        try
        {
            // Espera el bloqueo real de PostgreSQL antes de confirmar al ganador.
            while (!pendingSave.IsCompleted && !await firstContext.Database.SqlQuery<bool>(
                       $"SELECT {firstPid} = ANY(pg_blocking_pids({secondPid})) AS \"Value\"")
                       .SingleAsync(timeout.Token))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), timeout.Token);
            }

            pendingSave.IsCompleted.ShouldBeFalse();
            await firstTransaction.CommitAsync(timeout.Token);
            AssertLicenseConflict((await pendingSave).ShouldBeOfType<PersistenceConflictException>());
            await secondTransaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            if (!pendingSave.IsCompleted)
            {
                await timeout.CancelAsync();
                await pendingSave;
            }
        }

        firstContext.Database.CurrentTransaction.ShouldBeNull();
        secondContext.Database.CurrentTransaction.ShouldBeNull();
        await using var verification = Context();
        var winner = await verification.Doctors.AsNoTracking().Include(doctor => doctor.Specialties)
            .SingleAsync(doctor => seed.UserIds.Contains(doctor.UserId), timeout.Token);
        winner.UserId.ShouldBe(seed.UserIds[0]);
        winner.LicenseNumber.ShouldBe(license);
        winner.Specialties.ShouldHaveSingleItem().SpecialtyId.ShouldBe(seed.SpecialtyId);
        (await verification.Specialties.AnyAsync(value => value.Id == losingSpecialty.Id, timeout.Token)).ShouldBeFalse();
        (await verification.Database.SqlQuery<bool>(
            $"SELECT EXISTS (SELECT 1 FROM people.doctor_specialty WHERE specialty_id = {losingSpecialty.Id}) AS \"Value\"")
            .SingleAsync(timeout.Token)).ShouldBeFalse();
    }

    private async Task<DoctorRegistrationSeed> SeedRegistrationAsync(int userCount = 1,
        CancellationToken cancellationToken = default)
    {
        await using var context = Context();
        var users = Enumerable.Range(0, userCount).Select(_ => UserMother.WithRole(UserRole.Doctor,
            email: Email.Create($"registration-{Guid.NewGuid():N}@correo.com").Value)).ToArray();
        var specialty = Specialty.Create($"Especialidad {Guid.NewGuid():N}", "Especialidad inicial del perfil.").Value;
        context.Users.AddRange(users);
        context.Specialties.Add(specialty);
        await context.SaveChangesAsync(cancellationToken);
        var location = await context.Database.SqlQueryRaw<DoctorLocation>(
            "SELECT province_id, id AS municipality_id FROM people.municipality ORDER BY id LIMIT 1")
            .SingleAsync(cancellationToken);

        return new(users.Select(user => user.Id).ToArray(), specialty.Id, location.ProvinceId, location.MunicipalityId);
    }

    private static void AssertLicenseConflict(PersistenceConflictException conflict)
    {
        conflict.Error.ShouldBe(DoctorErrors.LicenseNumberAlreadyInUse);
        var postgres = AssertPostgresFailure(conflict.InnerException.ShouldBeOfType<DbUpdateException>(),
            PostgresErrorCodes.UniqueViolation);
        postgres.ConstraintName.ShouldBe("uq_doctor_license_number");
    }

    private static PostgresException AssertPostgresFailure(DbUpdateException failure, string sqlState)
    {
        var postgres = failure.InnerException.ShouldBeOfType<PostgresException>();
        postgres.SqlState.ShouldBe(sqlState);
        postgres.SchemaName.ShouldBe("people");
        postgres.TableName.ShouldBe("doctor");
        return postgres;
    }

    private sealed record DoctorLocation(int ProvinceId, int MunicipalityId);

    private sealed record DoctorRegistrationSeed(int[] UserIds, int SpecialtyId, int ProvinceId, int MunicipalityId)
    {
        public Doctor CreateDoctor(int userIndex, string licenseNumber, int? specialtyId = null) =>
            DoctorMother.Create([specialtyId ?? SpecialtyId], userId: UserIds[userIndex], provinceId: ProvinceId,
                municipalityId: MunicipalityId, licenseNumber: licenseNumber).Value;
    }

    private static Task<string> ReadProfileRowAsync(AppDbContext context, int doctorId) =>
        context.Database.SqlQuery<string>(
            $"""SELECT row_to_json(d)::text AS "Value" FROM people.doctor d WHERE id = {doctorId}""").SingleAsync();

    private AppDbContext Context() => DoctorPersistenceData.Context(Database.ConnectionString);
}

internal sealed record DoctorSeed(int UserId, int DoctorId, int[] SpecialtyIds);

internal static class DoctorPersistenceData
{
    public static AppDbContext Context(string connectionString) => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseNpgsql(connectionString).UseSnakeCaseNamingConvention().Options);

    public static ServiceProvider Provider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
        services.Configure<DatabaseOptions>(options => options.SgcmDatabase = connectionString);
        services.AddInfrastructure();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public static async Task<DoctorSeed> SeedAsync(AppDbContext context, int associations = 2, bool active = true)
    {
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"doctor-{Guid.NewGuid():N}@correo.com").Value);
        context.Users.Add(user);
        var specialties = Enumerable.Range(0, 3)
            .Select(index => Specialty.Create($"Especialidad {Guid.NewGuid():N}", "Descripción del catálogo.").Value).ToArray();
        specialties[1].Deactivate().IsSuccess.ShouldBeTrue();
        context.Specialties.AddRange(specialties);
        await context.SaveChangesAsync();
        var doctorId = await TestDataSeeder.InsertDoctorProfileAsync(context, user.Id);
        // IDs distintos para detectar una resolución errónea por Doctor.Id en lugar de UserId.
        var userId = user.Id;
        if (userId == doctorId)
        {
            var replacement = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"owner-{Guid.NewGuid():N}@correo.com").Value);
            context.Users.Add(replacement);
            await context.SaveChangesAsync();
            userId = replacement.Id;
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE people.doctor SET user_id = {userId} WHERE id = {doctorId}");
        }
        foreach (var specialty in specialties.Take(associations))
            await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialty.Id);
        if (!active)
            await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE people.doctor SET is_active = false WHERE id = {doctorId}");
        return new DoctorSeed(userId, doctorId, specialties.Select(value => value.Id).ToArray());
    }
}
