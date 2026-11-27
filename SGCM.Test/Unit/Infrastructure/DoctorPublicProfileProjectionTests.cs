using SGCM.Data.Application.People.Queries.GetDoctorProfile;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Data.Infrastructure.Persistence.Readers;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public sealed class DoctorPublicProfileProjectionTests
{
    private static readonly DoctorSearchDto Row = new(
        7, "  Laura ", " Méndez  ", 1, "Distrito Nacional", 2, "Santo Domingo de Guzmán", "Av. Abraham Lincoln 100",
        2500m, 12, "Cardióloga con enfoque preventivo.", [3, 5], ["Cardiología", "Medicina Interna"]);

    [Fact]
    public void Convierte_la_fila_del_directorio_en_el_perfil_publico()
    {
        var profile = DoctorPublicProfileProjection.Map(Row);

        // La lista se compara aparte: un record con un arreglo no compara su contenido.
        profile.Specialties.ShouldBe(new DoctorPublicProfileDto.Reference[] { new(3, "Cardiología"), new(5, "Medicina Interna") });
        profile.ShouldBe(new DoctorPublicProfileDto(
            7,
            "Laura Méndez",
            profile.Specialties,
            new(1, "Distrito Nacional"),
            new(2, "Santo Domingo de Guzmán"),
            "Av. Abraham Lincoln 100",
            2500m,
            12,
            "Cardióloga con enfoque preventivo."));
    }

    [Fact]
    public void Un_medico_sin_especialidades_activas_tiene_la_lista_vacia()
    {
        DoctorPublicProfileProjection.Map(Row with { SpecialtyIds = [], SpecialtyNames = [] }).Specialties.ShouldBeEmpty();
    }
}
