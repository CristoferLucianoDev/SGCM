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
