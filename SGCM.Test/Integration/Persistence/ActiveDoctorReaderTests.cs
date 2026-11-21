using Microsoft.EntityFrameworkCore;
using SGCM.Data.Infrastructure.Persistence.Readers;
using SGCM.Test.Common.Database;
using Shouldly;

namespace SGCM.Test.Integration.Persistence;

[Trait("Category", "Integration")]
public sealed class ActiveDoctorReaderTests(PostgresFixture database) : IntegrationTestBase(database)
{
    [Fact]
    public async Task FindActiveDoctorIdAsync_con_un_medico_activo_devuelve_su_id()
    {
        var seed = await SeedAsync(active: true);

        (await FindByDoctorIdAsync(seed.DoctorId)).ShouldBe(seed.DoctorId);
    }

    [Fact]
    public async Task FindActiveDoctorIdAsync_con_un_medico_inactivo_devuelve_null()
    {
        var seed = await SeedAsync(active: false);

        (await FindByDoctorIdAsync(seed.DoctorId)).ShouldBeNull();
    }

    [Fact]
    public async Task FindActiveDoctorIdAsync_con_un_id_inexistente_devuelve_null()
    {
        await SeedAsync(active: true);

        (await FindByDoctorIdAsync(int.MaxValue)).ShouldBeNull();
    }

    [Fact]
    public async Task FindActiveDoctorIdByUserIdAsync_con_el_usuario_de_un_medico_activo_devuelve_el_id_del_perfil()
    {
        var seed = await SeedAsync(active: true);

        (await FindByUserIdAsync(seed.UserId)).ShouldBe(seed.DoctorId);
    }

    [Fact]
    public async Task FindActiveDoctorIdByUserIdAsync_con_un_medico_inactivo_devuelve_null()
    {
        var seed = await SeedAsync(active: false);

        (await FindByUserIdAsync(seed.UserId)).ShouldBeNull();
    }

    [Fact]
    public async Task FindActiveDoctorIdByUserIdAsync_con_un_usuario_inexistente_devuelve_null()
    {
        await SeedAsync(active: true);

        (await FindByUserIdAsync(int.MaxValue)).ShouldBeNull();
    }

    [Fact]
    public async Task FindActiveDoctorIdByUserIdAsync_devuelve_el_perfil_del_usuario_y_no_el_de_otro_medico()
    {
        var first = await SeedAsync(active: true);
        var second = await SeedAsync(active: true);

        (await FindByUserIdAsync(first.UserId)).ShouldBe(first.DoctorId);
        (await FindByUserIdAsync(second.UserId)).ShouldBe(second.DoctorId);
    }

    [Fact]
    public async Task FindActiveDoctorIdByUserIdAsync_no_confunde_el_id_del_perfil_con_el_id_del_usuario()
    {
        var seed = await SeedAsync(active: true);

        // Con un solo médico, el id del perfil (distinto del id del usuario) no corresponde a ningún usuario con perfil.
        (await FindByUserIdAsync(seed.DoctorId)).ShouldBeNull();
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_devuelve_solo_los_medicos_activos_de_la_especialidad()
    {
        var first = await SeedWithAsync(associations: 1);
        var second = await SeedWithAsync(associations: 0);
        await LinkAsync(second.DoctorId, first.SpecialtyIds[0]);

        var ids = await ListBySpecialtyAsync(first.SpecialtyIds[0]);

        ids.ShouldBe(new[] { first.DoctorId, second.DoctorId });
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_no_incluye_a_un_medico_inactivo()
    {
        var active = await SeedWithAsync(associations: 1);
        var inactive = await SeedWithAsync(associations: 0, active: false);
        await LinkAsync(inactive.DoctorId, active.SpecialtyIds[0]);

        (await ListBySpecialtyAsync(active.SpecialtyIds[0])).ShouldBe(new[] { active.DoctorId });
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_con_una_especialidad_inactiva_devuelve_una_lista_vacia()
    {
        // SeedAsync deja la segunda especialidad inactiva y asocia las dos primeras al médico activo.
        var seed = await SeedWithAsync(associations: 2);

        (await ListBySpecialtyAsync(seed.SpecialtyIds[1])).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_con_una_especialidad_inexistente_devuelve_una_lista_vacia()
    {
        await SeedWithAsync(associations: 1);

        (await ListBySpecialtyAsync(int.MaxValue)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_no_incluye_a_un_medico_de_otra_especialidad()
    {
        var withSpecialty = await SeedWithAsync(associations: 1);
        var other = await SeedWithAsync(associations: 0);
        await LinkAsync(other.DoctorId, withSpecialty.SpecialtyIds[2]);

        (await ListBySpecialtyAsync(withSpecialty.SpecialtyIds[0])).ShouldBe(new[] { withSpecialty.DoctorId });
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_un_medico_con_dos_especialidades_aparece_una_sola_vez_en_cada_una()
    {
        var seed = await SeedWithAsync(associations: 1);
        await LinkAsync(seed.DoctorId, seed.SpecialtyIds[2]);

        (await ListBySpecialtyAsync(seed.SpecialtyIds[0])).ShouldBe(new[] { seed.DoctorId });
        (await ListBySpecialtyAsync(seed.SpecialtyIds[2])).ShouldBe(new[] { seed.DoctorId });
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_los_devuelve_ordenados_por_id()
    {
        var first = await SeedWithAsync(associations: 1);
        var second = await SeedWithAsync(associations: 0);
        var third = await SeedWithAsync(associations: 0);
        await LinkAsync(third.DoctorId, first.SpecialtyIds[0]);
        await LinkAsync(second.DoctorId, first.SpecialtyIds[0]);

        var ids = await ListBySpecialtyAsync(first.SpecialtyIds[0]);

        ids.ShouldBe(ids.Order().ToArray());
        ids.ShouldBe(new[] { first.DoctorId, second.DoctorId, third.DoctorId });
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_filtra_por_provincia_municipio_o_ambos_con_AND()
    {
        var inBase = await SeedWithAsync(associations: 1);
        var elsewhere = await SeedWithAsync(associations: 0);
        await LinkAsync(elsewhere.DoctorId, inBase.SpecialtyIds[0]);
        var (baseProvince, baseMunicipality) = await LocationOfAsync(inBase.DoctorId);
        var (otherProvince, otherMunicipality) = await MoveToAnotherProvinceAsync(elsewhere.DoctorId, baseProvince);
        var specialtyId = inBase.SpecialtyIds[0];

        (await ListBySpecialtyAsync(specialtyId, provinceId: baseProvince)).ShouldBe(new[] { inBase.DoctorId });
        (await ListBySpecialtyAsync(specialtyId, provinceId: otherProvince)).ShouldBe(new[] { elsewhere.DoctorId });
        (await ListBySpecialtyAsync(specialtyId, municipalityId: baseMunicipality)).ShouldBe(new[] { inBase.DoctorId });
        (await ListBySpecialtyAsync(specialtyId, municipalityId: otherMunicipality)).ShouldBe(new[] { elsewhere.DoctorId });
        (await ListBySpecialtyAsync(specialtyId, otherProvince, otherMunicipality)).ShouldBe(new[] { elsewhere.DoctorId });
        // Provincia de uno y municipio del otro: el AND no deja pasar a ninguno.
        (await ListBySpecialtyAsync(specialtyId, baseProvince, otherMunicipality)).ShouldBeEmpty();
    }

    [Fact]
    public async Task ListActiveDoctorIdsBySpecialtyAsync_sin_filtros_devuelve_todos_los_de_la_especialidad_sin_importar_la_ubicacion()
    {
        var inBase = await SeedWithAsync(associations: 1);
        var elsewhere = await SeedWithAsync(associations: 0);
        await LinkAsync(elsewhere.DoctorId, inBase.SpecialtyIds[0]);
        var (baseProvince, _) = await LocationOfAsync(inBase.DoctorId);
        await MoveToAnotherProvinceAsync(elsewhere.DoctorId, baseProvince);

        var ids = await ListBySpecialtyAsync(inBase.SpecialtyIds[0]);

        ids.ShouldBe(new[] { inBase.DoctorId, elsewhere.DoctorId });
    }

    private async Task<DoctorSeed> SeedWithAsync(int associations, bool active = true)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        return await DoctorPersistenceData.SeedAsync(context, associations, active);
    }

    private async Task LinkAsync(int doctorId, int specialtyId)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        await TestDataSeeder.InsertDoctorSpecialtyAsync(context, doctorId, specialtyId);
    }

    private async Task<(int ProvinceId, int MunicipalityId)> LocationOfAsync(int doctorId)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        var provinceId = await context.Database.SqlQuery<int>(
            $"""SELECT province_id AS "Value" FROM people.doctor WHERE id = {doctorId}""").SingleAsync();
        var municipalityId = await context.Database.SqlQuery<int>(
            $"""SELECT municipality_id AS "Value" FROM people.doctor WHERE id = {doctorId}""").SingleAsync();
        return (provinceId, municipalityId);
    }

    /// <summary>Mueve al médico al primer municipio de una provincia distinta de <paramref name="currentProvinceId"/>.</summary>
    private async Task<(int ProvinceId, int MunicipalityId)> MoveToAnotherProvinceAsync(int doctorId, int currentProvinceId)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        var municipalityId = await context.Database.SqlQuery<int>(
            $"""
            SELECT id AS "Value" FROM people.municipality WHERE province_id <> {currentProvinceId} ORDER BY id LIMIT 1
            """).SingleAsync();
        var provinceId = await context.Database.SqlQuery<int>(
            $"""SELECT province_id AS "Value" FROM people.municipality WHERE id = {municipalityId}""").SingleAsync();
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE people.doctor SET province_id = {provinceId}, municipality_id = {municipalityId} WHERE id = {doctorId}");
        return (provinceId, municipalityId);
    }

    private async Task<IReadOnlyList<int>> ListBySpecialtyAsync(
        int specialtyId, int? provinceId = null, int? municipalityId = null)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        return await new ActiveDoctorReader(context).ListActiveDoctorIdsBySpecialtyAsync(
            specialtyId, provinceId, municipalityId, CancellationToken.None);
    }

    private async Task<DoctorSeed> SeedAsync(bool active)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        var seed = await DoctorPersistenceData.SeedAsync(context, active: active);
        seed.UserId.ShouldNotBe(seed.DoctorId);
        return seed;
    }

    private async Task<int?> FindByDoctorIdAsync(int doctorId)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        return await new ActiveDoctorReader(context).FindActiveDoctorIdAsync(doctorId, CancellationToken.None);
    }

    private async Task<int?> FindByUserIdAsync(int userId)
    {
        await using var context = DoctorPersistenceData.Context(Database.ConnectionString);
        return await new ActiveDoctorReader(context).FindActiveDoctorIdByUserIdAsync(userId, CancellationToken.None);
    }
}
