using System.Data.Common;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SGCM.Data.Application.Abstractions.Messaging;
using SGCM.Data.Application.Abstractions.Security;
using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Domain.Common;
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

namespace SGCM.Test.Integration.People;

[Trait("Category", "Integration")]
public sealed class GetDoctorProfileTests(PostgresFixture database) : ApiIntegrationTestBase(database)
{
    private const string Password = "Sintetica258";
    private JsonSerializerOptions JsonOptions =>
        Factory.Services.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value.JsonSerializerOptions;

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Clock.UtcNow = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public override async Task DisposeAsync()
    {
        try { await Database.ResetAsync(); }
        finally { await base.DisposeAsync(); }
    }

    [Fact]
    public async Task Devuelve_el_mismo_perfil_publico_y_solo_el_administrador_recibe_los_dos_campos_privados()
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.Location, [catalog.Later.Id, catalog.Inactive.Id, catalog.Earlier.Id],
            legacyName: true);
        var other = await SeedDoctorAsync(catalog.Location, [catalog.Inactive.Id], phone: "809-555-0299");
        var administrator = await LoginAdministratorAsync();

        var profile = (await ReadAsync(doctor.Id)).Value.ShouldBeOfType<DoctorPublicProfileDto>();
        profile.Id.ShouldBe(doctor.Id);
        profile.FullName.ShouldBe("Ana  María Núñez de Peña");
        profile.Specialties.Select(item => (item.Id, item.Name)).ShouldBe(new[]
        {
            (catalog.Earlier.Id, catalog.Earlier.Name), (catalog.Later.Id, catalog.Later.Name)
        });
        profile.Province.Id.ShouldBe(catalog.Location.ProvinceId);
        profile.Province.Name.ShouldBe(catalog.Location.ProvinceName);
        profile.Municipality.Id.ShouldBe(catalog.Location.MunicipalityId);
        profile.Municipality.Name.ShouldBe(catalog.Location.MunicipalityName);
        profile.AddressLine.ShouldBe(doctor.AddressLine);
        profile.ConsultationFee.ShouldBe(doctor.ConsultationFee.Value);
        profile.YearsOfExperience.ShouldBe(doctor.YearsOfExperience);
        profile.Biography.ShouldBe(doctor.Biography);

        var publicJson = AssertPublicContract(profile);
        var privateProfile = (await ReadAsync(doctor.Id, administrator.Token)).Value.ShouldBeOfType<DoctorAdminProfileDto>();
        privateProfile.PhoneNumber.ShouldBe(doctor.PhoneNumber.Value);
        privateProfile.LicenseNumber.ShouldBe(doctor.LicenseNumber);
        var adminJson = Serialize(privateProfile);
        adminJson.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "profile", "phoneNumber", "licenseNumber" });
        adminJson.GetProperty("profile").GetRawText().ShouldBe(publicJson.GetRawText());
        adminJson.GetProperty("phoneNumber").GetString().ShouldBe(doctor.PhoneNumber.Value);
        adminJson.GetProperty("licenseNumber").GetString().ShouldBe(doctor.LicenseNumber);
        Serialize<DoctorProfileResult>(profile).EnumerateObject().ShouldBeEmpty();
        Serialize<DoctorProfileResult>(privateProfile).EnumerateObject().ShouldBeEmpty();

        var otherProfile = (await ReadAsync(other.Id, administrator.Token)).Value.ShouldBeOfType<DoctorAdminProfileDto>();
        otherProfile.Profile.Id.ShouldBe(other.Id);
        otherProfile.Profile.Specialties.ShouldBeEmpty();
        otherProfile.PhoneNumber.ShouldBe(other.PhoneNumber.Value);
        otherProfile.LicenseNumber.ShouldBe(other.LicenseNumber);
        otherProfile.LicenseNumber.ShouldNotBe(privateProfile.LicenseNumber);
    }

    [Fact]
    public async Task Usa_el_mismo_NotFound_para_medico_ausente_inactivo_o_con_cuenta_inactiva()
    {
        var catalog = await SeedCatalogAsync();
        var disabled = await SeedDoctorAsync(catalog.Location, [catalog.Earlier.Id], doctorActive: false);
        var blocked = await SeedDoctorAsync(catalog.Location, [catalog.Earlier.Id], accountActive: false);
        var administrator = await LoginAdministratorAsync();

        foreach (var id in new[] { int.MaxValue, disabled.Id, blocked.Id })
        foreach (var token in new[] { null, administrator.Token })
        {
            var result = await ReadAsync(id, token);
            result.Error.ShouldBe(DoctorErrors.PublicProfileNotFound);
            result.Error.Type.ShouldBe(ErrorType.NotFound);
        }
    }

    [Theory]
    [InlineData("revocada")]
    [InlineData("vencida")]
    [InlineData("ajena")]
    [InlineData("inactiva")]
    [InlineData("sin-confirmar")]
    [InlineData("cambio-pendiente")]
    [InlineData("rol-cambiado")]
    public async Task Un_JWT_administrativo_valido_solo_recibe_datos_publicos_si_la_base_rechaza_el_acceso(string state)
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.Location, [catalog.Earlier.Id]);
        var administrator = await LoginAdministratorAsync();
        var token = administrator.Token;
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        switch (state)
        {
            case "revocada":
                var reason = "logout";
                await db.Database.ExecuteSqlAsync($"""
                    UPDATE app.user_session SET revoked_at = {Clock.UtcNow}, revocation_reason = {reason}
                    WHERE id = {administrator.SessionId}
                    """);
                break;
            case "vencida":
                Clock.UtcNow = Clock.UtcNow.AddSeconds(1);
                await db.Database.ExecuteSqlAsync($"""
                    UPDATE app.user_session SET expires_at = {Clock.UtcNow}
                    WHERE id = {administrator.SessionId}
                    """);
                break;
            case "ajena":
                var other = await LoginAdministratorAsync();
                token = scope.ServiceProvider.GetRequiredService<ITokenService>()
                    .CreateAccessToken(administrator.UserId, UserRole.Administrator, other.SessionId, null).Value;
                break;
            case "inactiva":
                await db.Users.Where(user => user.Id == administrator.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
                break;
            case "sin-confirmar":
                await db.Users.Where(user => user.Id == administrator.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsEmailConfirmed, false));
                break;
            case "cambio-pendiente":
                await db.Users.Where(user => user.Id == administrator.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.MustChangePassword, true));
                break;
            case "rol-cambiado":
                await db.Users.Where(user => user.Id == administrator.UserId)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(user => user.Role, UserRole.Patient));
                break;
        }

        var profile = (await ReadAsync(doctor.Id, token)).Value.ShouldBeOfType<DoctorPublicProfileDto>();
        profile.Id.ShouldBe(doctor.Id);
        AssertPublicContract(profile);
    }

    [Fact]
    public async Task Una_identidad_incompleta_o_un_bearer_invalido_solo_ven_el_perfil_publico()
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.Location, [catalog.Earlier.Id]);
        var administrator = await LoginAdministratorAsync();
        var parts = administrator.Token.Split('.');
        parts[2] = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        var incomplete = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AppClaims.Role, nameof(UserRole.Administrator)), new Claim(AppClaims.UserId, administrator.UserId.ToString())],
            "Bearer", AppClaims.UserId, AppClaims.Role));
        var duplicate = new ClaimsPrincipal(new ClaimsIdentity(incomplete.Claims, "Bearer", AppClaims.UserId, AppClaims.Role));
        var sessionClaim = new Claim(AppClaims.SessionId, administrator.SessionId.ToString());
        duplicate.Identities.Single().AddClaim(sessionClaim);
        duplicate.Identities.Single().AddClaim(sessionClaim);
        var multiple = new ClaimsPrincipal(incomplete.Identities.Append(new ClaimsIdentity([sessionClaim], "Bearer")));
        var repeatedRole = new ClaimsPrincipal(new ClaimsIdentity(
            incomplete.Claims.Append(sessionClaim).Append(new Claim(AppClaims.Role, nameof(UserRole.Administrator))),
            "Bearer", AppClaims.UserId, AppClaims.Role));
        var conflictingRole = new ClaimsPrincipal(new ClaimsIdentity(
            incomplete.Claims.Append(sessionClaim).Append(new Claim(AppClaims.Role, nameof(UserRole.Patient))),
            "Bearer", AppClaims.UserId, AppClaims.Role));

        AssertPublicContract((await ReadAsync(doctor.Id, string.Join('.', parts), expectValidToken: false))
            .Value.ShouldBeOfType<DoctorPublicProfileDto>());
        foreach (var principal in new[] { incomplete, duplicate, multiple, repeatedRole, conflictingRole })
            AssertPublicContract((await ReadAsync(doctor.Id, principal: principal)).Value.ShouldBeOfType<DoctorPublicProfileDto>());
    }

    [Fact]
    public async Task PostgreSQL_selecciona_por_id_y_combina_autorizacion_y_lectura_privada_en_una_sentencia()
    {
        var catalog = await SeedCatalogAsync();
        var doctor = await SeedDoctorAsync(catalog.Location, [catalog.Earlier.Id]);
        var administrator = await LoginAdministratorAsync();
        var capture = new CommandCapture();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(Database.ConnectionString).UseSnakeCaseNamingConvention().AddInterceptors(capture).Options);
        var reader = new DoctorProfileReader(db);
        using var cancellation = new CancellationTokenSource();
        var ct = cancellation.Token;

        (await reader.GetPublicAsync(doctor.Id, ct))!.Id.ShouldBe(doctor.Id);
        var publicCommand = capture.Commands.ShouldHaveSingleItem();
        publicCommand.Sql.ShouldContain("people.vw_doctor_directory");
        foreach (var forbidden in new[] { "phone_number", "license_number", "user_id", "user_session", "app.", "people.doctor" })
            publicCommand.Sql.ShouldNotContain(forbidden);
        publicCommand.Parameters.ShouldContain(doctor.Id);
        publicCommand.Token.ShouldBe(ct);
        capture.Commands.Clear();

        var result = await reader.GetAdministrativeAsync(doctor.Id, administrator.UserId, administrator.SessionId, Clock.UtcNow, ct);
        result!.Profile.Id.ShouldBe(doctor.Id);
        result.PhoneNumber.ShouldBe(doctor.PhoneNumber.Value);
        var adminCommand = capture.Commands.ShouldHaveSingleItem();
        foreach (var expected in new[] { "people.vw_doctor_directory", "people.doctor", "app.user_session", "phone_number",
                     "license_number", "revoked_at", "expires_at", "is_email_confirmed", "must_change_password", "role_id" })
            adminCommand.Sql.ShouldContain(expected);
        foreach (var parameter in new object[] { doctor.Id, administrator.UserId, administrator.SessionId, Clock.UtcNow })
            adminCommand.Parameters.ShouldContain(parameter);
        adminCommand.Token.ShouldBe(ct);
        db.ChangeTracker.Entries().ShouldBeEmpty();
        (await db.Doctors.AsNoTracking().SingleAsync(row => row.Id == doctor.Id)).PhoneNumber.ShouldBe(doctor.PhoneNumber);
    }

    [Fact]
    public async Task Lee_el_perfil_actualizado_por_el_PUT_despues_del_commit_sin_cambiar_identidad_ni_especialidades()
    {
        var catalog = await SeedCatalogAsync();
        const string email = "doctor-put258@example.com";
        var doctor = await SeedDoctorAsync(catalog.Location, [catalog.Later.Id, catalog.Earlier.Id], email: email);
        var administrator = await LoginAdministratorAsync();
        var before = (await ReadAsync(doctor.Id, administrator.Token)).Value.ShouldBeOfType<DoctorAdminProfileDto>();
        var login = await LoginSuccessfullyAsync(email, Password);
        var updated = new
        {
            phoneNumber = "809-555-0258",
            catalog.AlternateLocation.ProvinceId,
            catalog.AlternateLocation.MunicipalityId,
            addressLine = "Nueva dirección",
            consultationFee = 38.19m,
            yearsOfExperience = 12,
            biography = "Nueva biografía"
        };
        using var request = new HttpRequestMessage(HttpMethod.Put, "/api/doctors/me/profile");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        request.Content = JsonContent.Create(updated);
        using var response = await Client.SendAsync(request);
        await response.ShouldHaveStatusAsync(HttpStatusCode.NoContent);

        var after = (await ReadAsync(doctor.Id, administrator.Token)).Value.ShouldBeOfType<DoctorAdminProfileDto>();
        after.PhoneNumber.ShouldBe(updated.phoneNumber);
        after.PhoneNumber.ShouldNotBe(before.PhoneNumber);
        after.LicenseNumber.ShouldBe(before.LicenseNumber);
        after.Profile.Id.ShouldBe(before.Profile.Id);
        after.Profile.FullName.ShouldBe(before.Profile.FullName);
        after.Profile.Specialties.ShouldBe(before.Profile.Specialties);
        after.Profile.Province.Name.ShouldBe(catalog.AlternateLocation.ProvinceName);
        after.Profile.Province.Id.ShouldBe(updated.ProvinceId);
        after.Profile.Municipality.Name.ShouldBe(catalog.AlternateLocation.MunicipalityName);
        after.Profile.Municipality.Id.ShouldBe(updated.MunicipalityId);
        after.Profile.AddressLine.ShouldBe(updated.addressLine);
        after.Profile.ConsultationFee.ShouldBe(updated.consultationFee);
        after.Profile.YearsOfExperience.ShouldBe(updated.yearsOfExperience);
        after.Profile.Biography.ShouldBe(updated.biography);
        var published = (await ReadAsync(doctor.Id)).Value.ShouldBeOfType<DoctorPublicProfileDto>();
        AssertPublicContract(published).GetRawText().ShouldBe(Serialize(after.Profile).GetRawText());
    }

    private async Task<Result<DoctorProfileResult>> ReadAsync(
        int doctorId, string? token = null, bool expectValidToken = true, ClaimsPrincipal? principal = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var original = accessor.HttpContext;
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        accessor.HttpContext = context;
        try
        {
            if (token is not null)
            {
                context.Request.Headers.Authorization = $"Bearer {token}";
                var authenticated = await context.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                authenticated.Succeeded.ShouldBe(expectValidToken);
                principal = authenticated.Principal;
            }
            context.User = principal ?? new ClaimsPrincipal();
            var handler = scope.ServiceProvider.GetRequiredService<IQueryHandler<GetDoctorProfileQuery, DoctorProfileResult>>();
            return await handler.HandleAsync(new GetDoctorProfileQuery(doctorId), CancellationToken.None);
        }
        finally { accessor.HttpContext = original; }
    }

    private async Task<Administrator> LoginAdministratorAsync()
    {
        var email = $"admin258-{Guid.NewGuid():N}@example.com";
        await SaveUserAsync(UserRole.Administrator, email, Password, false);
        var login = await LoginSuccessfullyAsync(email, Password);
        var user = (await FindUserAsync(email))!;
        return new(user.Id, user.Sessions.ShouldHaveSingleItem().Id, login.AccessToken.ShouldNotBeNull());
    }

    private async Task<Catalog> SeedCatalogAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var locations = await db.Database.SqlQuery<Location>($"""
            SELECT p.id AS province_id, p.name AS province_name, m.id AS municipality_id, m.name AS municipality_name
            FROM people.municipality m JOIN people.province p ON p.id = m.province_id
            ORDER BY m.id LIMIT 2
            """).ToListAsync();
        var later = Specialty.Create("Cardiología Pediátrica", "Especialidad activa.").Value;
        var inactive = Specialty.Create("Cardiología Clínica", "Especialidad inactiva.").Value;
        inactive.Deactivate().IsSuccess.ShouldBeTrue();
        var earlier = Specialty.Create("Cardiología", "Especialidad activa.").Value;
        db.Specialties.AddRange(later, inactive, earlier);
        await db.SaveChangesAsync();
        return new(locations[0], locations[1], earlier, later, inactive);
    }

    private async Task<Doctor> SeedDoctorAsync(Location location, int[] specialtyIds, string phone = "809-555-0201",
        string? email = null, bool doctorActive = true, bool accountActive = true, bool legacyName = false)
    {
        var user = UserMother.WithRole(UserRole.Doctor,
            email: Email.Create(email ?? $"doctor258-{Guid.NewGuid():N}@example.com").Value,
            passwordHash: Factory.HashPassword(Password));
        if (!accountActive) user.Deactivate().IsSuccess.ShouldBeTrue();
        await SaveAsync(user);
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var doctor = DoctorMother.Create(specialtyIds, userId: user.Id, firstName: " Ana  María ",
            lastName: " Núñez de Peña ", licenseNumber: $"LIC-258-{user.Id}", phoneNumber: phone,
            provinceId: location.ProvinceId, municipalityId: location.MunicipalityId).Value;
        if (!doctorActive) doctor.Deactivate().IsSuccess.ShouldBeTrue();
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync();
        if (legacyName)
        {
            var firstName = $" {doctor.FirstName} ";
            var lastName = $" {doctor.LastName} ";
            await db.Doctors.Where(row => row.Id == doctor.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.FirstName, firstName)
                .SetProperty(row => row.LastName, lastName));
        }
        return doctor;
    }

    private JsonElement AssertPublicContract(DoctorPublicProfileDto profile)
    {
        var shape = new[] { "Id", "FullName", "Specialties", "Province", "Municipality", "AddressLine",
            "ConsultationFee", "YearsOfExperience", "Biography" };
        typeof(DoctorPublicProfileDto).GetProperties().Select(p => p.Name).ShouldBe(shape, ignoreOrder: true);
        var json = Serialize(profile);
        json.EnumerateObject().Select(p => p.Name)
            .ShouldBe(shape.Select(JsonNamingPolicy.CamelCase.ConvertName), ignoreOrder: true);
        json.GetProperty("fullName").GetString().ShouldBe(profile.FullName);
        return json;
    }

    private JsonElement Serialize<T>(T value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    private sealed record Administrator(int UserId, int SessionId, string Token);
    private sealed record Catalog(Location Location, Location AlternateLocation, Specialty Earlier, Specialty Later, Specialty Inactive);
    private sealed record Location(int ProvinceId, string ProvinceName, int MunicipalityId, string MunicipalityName);
    private sealed record ObservedCommand(string Sql, object?[] Parameters, CancellationToken Token);

    private sealed class CommandCapture : DbCommandInterceptor
    {
        public List<ObservedCommand> Commands { get; } = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            Record(command, cancellationToken);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            int result, CancellationToken cancellationToken = default)
        {
            Record(command, cancellationToken);
            return ValueTask.FromResult(result);
        }

        private void Record(DbCommand command, CancellationToken cancellationToken) =>
            Commands.Add(new(command.CommandText, command.Parameters.Cast<DbParameter>().Select(p => p.Value).ToArray(),
                cancellationToken));
    }
}
