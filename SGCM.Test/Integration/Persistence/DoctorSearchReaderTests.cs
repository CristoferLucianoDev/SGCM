using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Common;
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
        await AddDoctorAsync(db, location, "Ana", "Baja", [cardio.Id], doctorActive: false);
        await AddDoctorAsync(db, location, "Ana", "Bloqueada", [cardio.Id], accountActive: false);
        var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<SearchDoctorsQuery, PagedResult<DoctorSearchDto>>>();

        foreach (var search in new[] { null, "", " \t ", "ANA" })
        {
            var page = (await handler.HandleAsync(new SearchDoctorsQuery(search), CancellationToken.None)).Value;
            page.TotalCount.ShouldBe(2);
            page.Items.Select(item => item.Id).ShouldBe(new[] { visible.Id, withoutVisibleSpecialties.Id });
            var item = page.Items[0];
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
            page.Items[1].SpecialtyIds.ShouldBeEmpty();
            page.Items[1].SpecialtyNames.ShouldBeEmpty();
            typeof(DoctorSearchDto).GetProperties().Select(property => property.Name).ShouldBe(new[]
            {
                "Id", "FirstName", "LastName", "ProvinceId", "ProvinceName", "MunicipalityId", "MunicipalityName",
                "AddressLine", "ConsultationFee", "YearsOfExperience", "Biography", "SpecialtyIds", "SpecialtyNames"
            }, ignoreOrder: true);
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

        var page = await reader.SearchAsync(search, 1, 1, ct);

        page.TotalCount.ShouldBe(1);
        page.Items.ShouldHaveSingleItem().Id.ShouldBe(literal.Id);
        capture.Commands.Count.ShouldBe(2);
        foreach (var command in capture.Commands)
        {
            command.Sql.ShouldContain("people.vw_doctor_directory");
            command.Sql.ShouldContain("strpos(");
            command.Sql.ShouldContain("people.sp_text_normalize(@");
            command.Sql.ShouldNotContain(search);
            command.Parameters.ShouldContain(search);
            command.Token.ShouldBe(ct);
        }
        foreach (var text in new[] { "%", "_", "'", "\\" })
            (await reader.SearchAsync(text, 1, 20, ct)).Items.ShouldHaveSingleItem().Id.ShouldBe(literal.Id);
        (await reader.SearchAsync("o%control", 1, 20, ct)).Items.ShouldBeEmpty();
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
    private sealed record ObservedCommand(string Sql, object?[] Parameters, CancellationToken Token);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<ObservedCommand> Commands { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Commands.Add(new(command.CommandText,
                command.Parameters.Cast<DbParameter>().Select(parameter => parameter.Value).ToArray(), cancellationToken));
            return ValueTask.FromResult(result);
        }
    }
}
