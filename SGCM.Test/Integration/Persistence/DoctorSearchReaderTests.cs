using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Data.Domain.Common.ValueObjects;
using SGCM.Data.Domain.Identity.Users;
using SGCM.Data.Domain.People.Doctors;
using SGCM.Data.Domain.People.Specialties;
using SGCM.Data.Infrastructure.Persistence;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using SGCM.Test.Common.Mothers;
using SGCM.Test.Common.Web;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class DoctorSearchReaderTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    [Fact]
    public async Task Busca_fragmentos_del_nombre_y_del_apellido_sin_distinguir_mayusculas_ni_tildes()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var location = await ReadLocationAsync(db);
        var jose = await AddDoctorAsync(db, location, "José", "Martínez");
        var joseWithoutAccent = await AddDoctorAsync(db, location, "Jose", "Ramos");
        var maria = await AddDoctorAsync(db, location, "María", "Gómez");
        var andres = await AddDoctorAsync(db, location, "Andrés", "Pérez");
        var carolina = await AddDoctorAsync(db, location, "Carolina", "Rodríguez");
        var ana = await AddDoctorAsync(db, location, "Ana  María", "De la Cruz");
        var ines = await AddDoctorAsync(db, location, "Ine\u0301s", "Lo\u0301pez");
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>>();
        (string Search, int[] Expected)[] cases =
        [
            ("jose", [jose.Id, joseWithoutAccent.Id]),
            ("José Mar", [jose.Id]),
            ("Jose\u0301 Marti\u0301nez", [jose.Id]),
            ("  JOSE mar  ", [jose.Id]),
            ("maria", [maria.Id, ana.Id]),
            ("MARIA GOMEZ", [maria.Id]),
            ("andres", [andres.Id]),
            ("lina", [carolina.Id]),
            ("MARTINEZ", [jose.Id]),
            ("Inés Lopez", [ines.Id]),
            ("maria de la", [ana.Id]),
            ("ana  maria", [ana.Id]),
            ("ana maria", []),
            ("martinez jose", []),
            ("nombre inexistente", [])
        ];
        foreach (var (search, expected) in cases)
        {
            var page = (await handler.HandleAsync(new SearchDoctorsQuery(search), CancellationToken.None)).Value;
            page.Items.Select(doctor => doctor.Id).ShouldBe(expected, ignoreOrder: true);
            page.TotalCount.ShouldBe(expected.Length);
        }
    }

    [Fact]
    public async Task La_vista_filtra_visibilidad_y_devuelve_solo_la_proyeccion_autorizada()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var location = await ReadLocationAsync(db);
        var trauma = Specialty.Create("Traumatología 257", "Activa.").Value;
        var cardio = Specialty.Create("Cardiología 257", "Activa.").Value;
        var inactive = Specialty.Create("Medicina 257", "Inactiva.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        db.Specialties.AddRange(trauma, cardio, inactive);
        await db.SaveChangesAsync();
        var visible = await AddDoctorAsync(db, location, "Ana", "Luna", [trauma.Id, cardio.Id, inactive.Id]);
        var withoutVisibleSpecialties = await AddDoctorAsync(db, location, "Ana", "Prado", [inactive.Id]);
        var withoutAssociations = await AddDoctorAsync(db, location, "Ana", "Sin especialidades", [cardio.Id]);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM people.doctor_specialty WHERE doctor_id = {withoutAssociations.Id}");
        await AddDoctorAsync(db, location, "Ana", "Baja", [cardio.Id], doctorActive: false);
        await AddDoctorAsync(db, location, "Ana", "Bloqueada", [cardio.Id], accountActive: false);
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>>();

        foreach (var search in new[] { null, "", " \t ", "ANA" })
        {
            var page = (await handler.HandleAsync(new SearchDoctorsQuery(search), CancellationToken.None)).Value;
            page.TotalCount.ShouldBe(1);
            var item = page.Items.ShouldHaveSingleItem();
            item.Id.ShouldBe(visible.Id);
            item.FirstName.ShouldBe(visible.FirstName);
            item.LastName.ShouldBe(visible.LastName);
            item.ProvinceId.ShouldBe(location.ProvinceId);
            item.ProvinceName.ShouldBe(location.ProvinceName);
            item.MunicipalityId.ShouldBe(location.MunicipalityId);
            item.MunicipalityName.ShouldBe(location.MunicipalityName);
            item.AddressLine.ShouldBe(visible.AddressLine);
            item.ConsultationFee.ShouldBe(visible.ConsultationFee.Value);
            item.YearsOfExperience.ShouldBe(visible.YearsOfExperience);
            item.Biography.ShouldBe(visible.Biography);
            item.SpecialtyIds.ShouldBe(new[] { cardio.Id, trauma.Id });
            item.SpecialtyNames.ShouldBe(new[] { cardio.Name, trauma.Name });
            typeof(DoctorSearchDto).GetProperties().Select(property => property.Name).ShouldBe(new[]
            {
                "Id", "FirstName", "LastName", "ProvinceId", "ProvinceName", "MunicipalityId", "MunicipalityName",
                "AddressLine", "ConsultationFee", "YearsOfExperience", "Biography", "SpecialtyIds", "SpecialtyNames"
            }, ignoreOrder: true);
        }

        var profileHandler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult>>();
        foreach (var doctor in new[] { withoutVisibleSpecialties, withoutAssociations })
        {
            var profile = (await profileHandler.HandleAsync(new(doctor.Id), CancellationToken.None)).Value
                .ShouldBeOfType<DoctorPublicProfileDto>();
            profile.Id.ShouldBe(doctor.Id);
            profile.Specialties.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Los_caracteres_especiales_son_literales_y_se_parametrizan_en_el_conteo_y_la_pagina()
    {
        var capture = new CommandCapture();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(capture).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var location = await ReadLocationAsync(db);
        var literal = await AddDoctorAsync(db, location, "Ana", "O'Brien%_\\Control");
        await AddDoctorAsync(db, location, "Ana", "OBrienXYControl");
        capture.Commands.Clear();
        var reader = new DoctorSearchReader(db);
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;
        const string search = "O'Brien%_\\";

        var criteria = new DoctorSearchCriteria(search, 1, 1, null, null, null);
        var page = await reader.SearchAsync(criteria, ct);

        page.TotalCount.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(literal.Id);
        capture.Commands.Count.ShouldBe(2);
        foreach (var command in capture.Commands)
        {
            command.Sql.ShouldContain("people.vw_doctor_directory");
            command.Sql.ShouldContain("strpos(");
            command.Sql.ShouldContain("people.sp_text_normalize(@");
            command.Sql.ShouldNotContain(search);
            command.Parameters.Values.ShouldContain(search);
            command.Token.ShouldBe(ct);
        }
        foreach (var text in new[] { "%", "_", "'", "\\" })
            (await reader.SearchAsync(criteria with { Search = text, PageSize = 20 }, ct))
                .Items.ShouldHaveSingleItem().Id.ShouldBe(literal.Id);
        (await reader.SearchAsync(criteria with { Search = "o%control", PageSize = 20 }, ct)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Combina_los_filtros_en_PostgreSQL_y_ejecuta_dos_comandos_por_busqueda()
    {
        var capture = new CommandCapture();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(capture).Options);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var locations = await db.Database.SqlQuery<Location>($"""
            SELECT p.id AS province_id, p.name AS province_name, m.id AS municipality_id, m.name AS municipality_name
            FROM people.municipality m JOIN people.province p ON p.id = m.province_id
            WHERE p.name IN ('Azua', 'Dajabón', 'Distrito Nacional')
            ORDER BY p.id, m.id
            """).ToListAsync();
        var home = locations.First(location => location.ProvinceName == "Azua");
        var sameProvince = locations.First(location =>
            location.ProvinceId == home.ProvinceId && location.MunicipalityId != home.MunicipalityId);
        var otherProvince = locations.First(location => location.ProvinceName == "Dajabón");
        var unused = locations.Single(location => location.ProvinceName == "Distrito Nacional");
        var cardio = Specialty.Create("Cardiología 234", "Activa.").Value;
        var internalMedicine = Specialty.Create("Medicina interna 234", "Activa.").Value;
        var inactive = Specialty.Create("Neurología 234", "Inactiva.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        db.Specialties.AddRange(cardio, internalMedicine, inactive);
        await db.SaveChangesAsync();

        int[] multiple = [internalMedicine.Id, cardio.Id, inactive.Id];
        var a = await AddDoctorAsync(db, home, "Ana", "Bravo", multiple);
        var b = await AddDoctorAsync(db, home, "Ana", "Bravo", [cardio.Id, internalMedicine.Id]);
        var c = await AddDoctorAsync(db, home, "Ana", "Cano", [internalMedicine.Id, inactive.Id]);
        var d = await AddDoctorAsync(db, sameProvince, "Ana", "Díaz", [cardio.Id]);
        var e = await AddDoctorAsync(db, otherProvince, "Ana", "Espino", [cardio.Id]);
        var f = await AddDoctorAsync(db, home, "Beatriz", "Bravo", [cardio.Id]);
        // Ordena antes del primer resultado elegible y obliga a filtrar antes de paginar.
        await AddDoctorAsync(db, home, "Ana", "Abad", [internalMedicine.Id], doctorActive: false);
        await AddDoctorAsync(db, home, "Ana", "Acosta", [cardio.Id], accountActive: false);
        await AddDoctorAsync(db, home, "Ana", "Alba", [inactive.Id]);
        var earlierNonMatch = await AddDoctorAsync(db, home, "Ana", "Araújo", [internalMedicine.Id]);

        var reader = new DoctorSearchReader(db);
        using var cancellation = new CancellationTokenSource();
        var criteria = new DoctorSearchCriteria(null, 1, 20, null, null, null);
        (int? Specialty, int? Province, int? Municipality, int[] Expected)[] cases =
        [
            (null, null, null, [earlierNonMatch.Id, a.Id, b.Id, c.Id, d.Id, e.Id, f.Id]),
            (cardio.Id, null, null, [a.Id, b.Id, d.Id, e.Id, f.Id]),
            (null, home.ProvinceId, null, [earlierNonMatch.Id, a.Id, b.Id, c.Id, d.Id, f.Id]),
            (null, null, home.MunicipalityId, [earlierNonMatch.Id, a.Id, b.Id, c.Id, f.Id]),
            (cardio.Id, home.ProvinceId, null, [a.Id, b.Id, d.Id, f.Id]),
            (cardio.Id, null, home.MunicipalityId, [a.Id, b.Id, f.Id]),
            (null, home.ProvinceId, home.MunicipalityId, [earlierNonMatch.Id, a.Id, b.Id, c.Id, f.Id]),
            (cardio.Id, home.ProvinceId, home.MunicipalityId, [a.Id, b.Id, f.Id])
        ];
        foreach (var (specialty, province, municipality, expected) in cases)
        {
            var filters = criteria with { SpecialtyId = specialty, ProvinceId = province, MunicipalityId = municipality };
            await AssertPageAsync(filters, expected, expected.Length);
            await AssertPageAsync(filters with { Search = "ANA" }, expected.Where(id => id != f.Id).ToArray(),
                expected.Length - 1);
        }

        var all = criteria with { Search = "ANA", SpecialtyId = cardio.Id,
            ProvinceId = home.ProvinceId, MunicipalityId = home.MunicipalityId };
        var full = await AssertPageAsync(all with { PageSize = 2 }, [a.Id, b.Id], 2);
        foreach (var item in full.Items)
        {
            item.SpecialtyIds.ShouldBe(new[] { cardio.Id, internalMedicine.Id });
            item.SpecialtyNames.ShouldBe(new[] { cardio.Name, internalMedicine.Name });
        }
        await AssertPageAsync(all with { PageSize = 1 }, [a.Id], 2);
        await AssertPageAsync(all with { PageNumber = 2, PageSize = 1 }, [b.Id], 2);
        await AssertPageAsync(all with { PageNumber = 3, PageSize = 1 }, [], 2);

        var threeMatches = all with { Search = null };
        await AssertPageAsync(threeMatches with { PageSize = 2 }, [a.Id, b.Id], 3);
        await AssertPageAsync(threeMatches with { PageNumber = 2, PageSize = 2 }, [f.Id], 3);
        await AssertPageAsync(threeMatches with { PageNumber = 2, PageSize = 1 }, [b.Id], 3);

        var absent = await db.Database.SqlQuery<MissingIds>($"""
            SELECT (SELECT max(id) + 1 FROM people.specialty) AS specialty_id,
                   (SELECT max(id) + 1 FROM people.province) AS province_id,
                   (SELECT max(id) + 1 FROM people.municipality) AS municipality_id
            """).SingleAsync();
        DoctorSearchCriteria[] noMatches =
        [
            criteria with { SpecialtyId = absent.SpecialtyId },
            criteria with { SpecialtyId = inactive.Id },
            criteria with { ProvinceId = absent.ProvinceId },
            criteria with { MunicipalityId = absent.MunicipalityId },
            criteria with { ProvinceId = unused.ProvinceId },
            criteria with { MunicipalityId = unused.MunicipalityId },
            criteria with { ProvinceId = otherProvince.ProvinceId, MunicipalityId = home.MunicipalityId },
            criteria with { Search = "nombre sin coincidencias" },
            criteria with { SpecialtyId = internalMedicine.Id, MunicipalityId = otherProvince.MunicipalityId }
        ];
        foreach (var filters in noMatches)
            await AssertPageAsync(filters, [], 0);

        async Task<PagedResult<DoctorSearchDto>> AssertPageAsync(DoctorSearchCriteria filters, int[] expected, int total)
        {
            capture.Commands.Clear();
            var page = await reader.SearchAsync(filters, cancellation.Token);
            page.Items.Select(item => item.Id).ShouldBe(expected);
            page.TotalCount.ShouldBe(total);
            page.PageNumber.ShouldBe(filters.PageNumber);
            page.PageSize.ShouldBe(filters.PageSize);
            AssertSearchCommands(capture.Commands, filters, cancellation.Token);
            return page;
        }
    }

    private static void AssertSearchCommands(
        IReadOnlyList<ObservedCommand> commands, DoctorSearchCriteria filters, CancellationToken token)
    {
        const string parameter = @"@(?<name>\w+)";
        commands.Count.ShouldBe(2);
        commands[0].Sql.ShouldContain("count(*)");
        commands[0].Sql.ShouldNotContain("LIMIT");
        commands[1].Sql.ShouldMatch(@"\bORDER BY\s+\w+\.first_name\s*,\s*\w+\.last_name\s*,\s*\w+\.id\b");
        AssertSqlParameter(commands[1], $@"\bLIMIT\s+{parameter}", filters.PageSize);
        AssertSqlParameter(commands[1], $@"\bOFFSET\s+{parameter}", (filters.PageNumber - 1) * filters.PageSize);
        foreach (var command in commands)
        {
            command.Token.ShouldBe(token);
            command.Sql.ShouldMatch(@"\b(?:cardinality\(\w+\.specialty_ids\)|array_length\(\w+\.specialty_ids,\s*1\))\s*>\s*0");
            AssertSqlParameter(command,
                $@"\bspecialty_ids\s*@>\s*ARRAY\[\s*{parameter}|{parameter}\s*=\s*ANY\s*\(\s*\w+\.specialty_ids\s*\)",
                filters.SpecialtyId);
            AssertSqlParameter(command, $@"\bprovince_id\s*=\s*{parameter}", filters.ProvinceId);
            AssertSqlParameter(command, $@"\bmunicipality_id\s*=\s*{parameter}", filters.MunicipalityId);
            AssertSqlParameter(command, $@"people\.sp_text_normalize\(\s*{parameter}", filters.Search);
        }
    }

    private static void AssertSqlParameter(ObservedCommand command, string pattern, object? value)
    {
        var match = Regex.Match(command.Sql, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        match.Success.ShouldBe(value is not null, $"Presencia inesperada del parámetro para {pattern}.");
        if (value is null) return;

        command.Parameters[match.Groups["name"].Value].ShouldBe(value);
    }

    [Fact]
    public async Task El_conteo_se_filtra_antes_de_paginar_y_el_id_desempata_nombres_iguales()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var location = await ReadLocationAsync(db);
        var last = await AddDoctorAsync(db, location, "Ana", "Zapata");
        var first = await AddDoctorAsync(db, location, "Ana", "Bravo");
        var second = await AddDoctorAsync(db, location, "Ana", "Bravo");
        var afterAna = await AddDoctorAsync(db, location, "Anabel", "Abarca");
        await AddDoctorAsync(db, location, "Abel", "Arias");
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>>();

        int[] expected = [first.Id, second.Id, last.Id, afterAna.Id];
        for (var i = 0; i < expected.Length; i++)
        {
            var page = (await handler.HandleAsync(new SearchDoctorsQuery("ana", i + 1, 1), CancellationToken.None)).Value;
            page.TotalCount.ShouldBe(expected.Length);
            page.PageNumber.ShouldBe(i + 1);
            page.PageSize.ShouldBe(1);
            page.Items.ShouldHaveSingleItem().Id.ShouldBe(expected[i]);
        }
        var beyond = (await handler.HandleAsync(new SearchDoctorsQuery("ana", expected.Length + 1, 1), CancellationToken.None)).Value;
        beyond.Items.ShouldBeEmpty();
        beyond.TotalCount.ShouldBe(expected.Length);
    }

    private static Task<Location> ReadLocationAsync(AppDbContext db) => db.Database.SqlQuery<Location>($"""
        SELECT p.id AS province_id, p.name AS province_name, m.id AS municipality_id, m.name AS municipality_name
        FROM people.municipality m JOIN people.province p ON p.id = m.province_id
        ORDER BY m.id LIMIT 1
        """).SingleAsync();

    private static async Task<Doctor> AddDoctorAsync(AppDbContext db, Location location, string firstName, string lastName,
        int[]? specialtyIds = null, bool doctorActive = true, bool accountActive = true)
    {
        var user = UserMother.WithRole(UserRole.Doctor, email: Email.Create($"doctor257-{Guid.NewGuid():N}@correo.com").Value);
        if (!accountActive) user.Deactivate().IsSuccess.ShouldBeTrue();
        db.Users.Add(user);
        if (specialtyIds is null)
        {
            var specialty = Specialty.Create($"Especialidad {Guid.NewGuid():N}", "Especialidad de prueba.").Value;
            db.Specialties.Add(specialty);
            await db.SaveChangesAsync();
            specialtyIds = [specialty.Id];
        }
        else
        {
            await db.SaveChangesAsync();
        }
        var doctor = DoctorMother.Create(specialtyIds, userId: user.Id, firstName: firstName, lastName: lastName,
            licenseNumber: $"LIC-257-{user.Id}", provinceId: location.ProvinceId, municipalityId: location.MunicipalityId).Value;
        if (!doctorActive) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        return doctor;
    }

    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record MissingIds(int SpecialtyId, int ProvinceId, int MunicipalityId);
    private sealed record ObservedCommand(
        string Sql, IReadOnlyDictionary<string, object?> Parameters, CancellationToken Token);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<ObservedCommand> Commands { get; } = [];

        public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        {
            Capture(command, CancellationToken.None);
            return result;
        }

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Capture(command, cancellationToken);
            return ValueTask.FromResult(result);
        }

        public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
        {
            Capture(command, CancellationToken.None);
            return result;
        }

        public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            object? result, CancellationToken cancellationToken = default)
        {
            Capture(command, cancellationToken);
            return ValueTask.FromResult(result);
        }

        public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
        {
            Capture(command, CancellationToken.None);
            return result;
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            Capture(command, cancellationToken);
            return ValueTask.FromResult(result);
        }

        private void Capture(DbCommand command, CancellationToken cancellationToken) =>
            Commands.Add(new(command.CommandText,
                command.Parameters.Cast<DbParameter>().ToDictionary(
                    parameter => parameter.ParameterName.TrimStart('@'), parameter => parameter.Value), cancellationToken));
    }
}
