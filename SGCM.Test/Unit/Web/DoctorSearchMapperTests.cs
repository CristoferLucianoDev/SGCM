using System.Text.Json;
using SGCM.Data.Application.Common;
using SGCM.Data.Application.People.Queries.SearchDoctors;
using SGCM.Web.Contracts.People;
using SGCM.Web.Mappers.People;
using Shouldly;

namespace SGCM.Test.Unit.Web;

public sealed class DoctorSearchMapperTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void El_request_conserva_los_seis_parametros_y_los_valores_predeterminados()
    {
        new SearchDoctorsRequest().ToQuery().ShouldBe(new SearchDoctorsQuery());

        const string search = "  Ána  Ñúñez  ";
        new SearchDoctorsRequest(Search: search, PageNumber: 3, PageSize: 7,
                SpecialtyId: 11, ProvinceId: 19, MunicipalityId: 31).ToQuery()
            .ShouldBe(new SearchDoctorsQuery(Search: search, PageNumber: 3, PageSize: 7,
                SpecialtyId: 11, ProvinceId: 19, MunicipalityId: 31));
    }

    [Fact]
    public void El_medico_conserva_nombre_ubicacion_tarifa_y_pares_de_especialidad_en_orden()
    {
        var doctor = CreateDoctor(37).ToResponse();

        doctor.Id.ShouldBe(37);
        doctor.FullName.ShouldBe("Ana  Mari\u0301a Núñez de la Cruz");
        doctor.Specialties.ShouldBe(new DoctorPublicProfileResponse.Reference[]
        {
            new(51, "Pediatría"), new(13, "Cardiología")
        });
        doctor.Province.ShouldBe(new ProvinceResponse(19, "Sánchez Ramírez"));
        doctor.Municipality.ShouldBe(new MunicipalityResponse(31, "Cotuí"));
        doctor.ConsultationFee.ShouldBe(1234.56m);

        var json = JsonSerializer.SerializeToElement(doctor, WebJson);
        AssertFields(json, "id", "fullName", "specialties", "province", "municipality", "consultationFee");
        AssertFields(json.GetProperty("province"), "id", "name");
        AssertFields(json.GetProperty("municipality"), "id", "name");
        foreach (var specialty in json.GetProperty("specialties").EnumerateArray())
        {
            AssertFields(specialty, "id", "name");
        }
    }

    [Fact]
    public void La_pagina_conserva_el_orden_y_los_metadatos_completos()
    {
        var page = new PagedResult<DoctorSearchDto>([CreateDoctor(37), CreateDoctor(23)], 3, 2, 17).ToResponse();

        page.Items.Select(doctor => doctor.Id).ShouldBe(new[] { 37, 23 });
        page.PageNumber.ShouldBe(3);
        page.PageSize.ShouldBe(2);
        page.TotalCount.ShouldBe(17);
        AssertFields(JsonSerializer.SerializeToElement(page, WebJson), "items", "pageNumber", "pageSize", "totalCount");
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(10, 5)]
    public void Una_pagina_sin_elementos_conserva_el_total_filtrado(int pageNumber, int totalCount)
    {
        var page = new PagedResult<DoctorSearchDto>([], pageNumber, 20, totalCount).ToResponse();

        page.Items.ShouldBeEmpty();
        page.PageNumber.ShouldBe(pageNumber);
        page.PageSize.ShouldBe(20);
        page.TotalCount.ShouldBe(totalCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Las_longitudes_inconsistentes_de_especialidades_se_rechazan(bool fewerIds)
    {
        var doctor = CreateDoctor(37);
        doctor = fewerIds ? doctor with { SpecialtyIds = [51] } : doctor with { SpecialtyNames = ["Pediatría"] };

        Should.Throw<InvalidOperationException>(() => doctor.ToResponse());
    }

    private static DoctorSearchDto CreateDoctor(int id) => new(
        id, "  Ana  Mari\u0301a ", " Núñez de la Cruz  ", 19, "Sánchez Ramírez", 31, "Cotuí",
        "Calle 199", 1234.56m, 9, "Biografía del perfil", [51, 13], ["Pediatría", "Cardiología"]);

    private static void AssertFields(JsonElement json, params string[] fields) =>
        json.EnumerateObject().Select(field => field.Name).ShouldBe(fields, ignoreOrder: true);
}
