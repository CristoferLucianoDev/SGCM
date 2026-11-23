using System.Reflection;
using NSubstitute;
using SGCM.Data.Application.People.Commands.UpdateInsuranceProvider;
using SGCM.Data.Domain.Common;
using SGCM.Data.Domain.Common.Interfaces;
using SGCM.Data.Domain.People.InsuranceProviders;
using SGCM.Test.Common.Mothers;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

using Unit = SGCM.Data.Domain.Common.Unit;

public sealed class UpdateInsuranceProviderCommandHandlerTests
{
    private const int ProviderId = 42;
    private const string NewName = "Seguros del Caribe";
    private const string NewPhone = "809-555-0999";
    private const string NewEmail = "nuevo@caribe.com.do";
    private const string NewAddress = "Calle Nueva 5, Santiago";
    private const string NewWebsite = "https://caribe.com.do";
    private const string BasePath = "https://sitio.com/";

    private static readonly string?[] MissingValues = [null, "", "   ", " \t\r\n "];

    private readonly IInsuranceProviderRepository _repository = Substitute.For<IInsuranceProviderRepository>();
    private readonly IDomainEvent _pendingEvent = Substitute.For<IDomainEvent>();

    private static Change<string?> Set(string? value) => Change<string?>.Set(value);

    public static IEnumerable<object[]> SelectedFields()
    {
        foreach (var active in new[] { true, false })
        {
            foreach (var field in new[] { "name", "phone", "email", "address", "website", "all" })
            {
                yield return [field, active];
            }
        }
    }

    [Theory]
    [MemberData(nameof(SelectedFields))]
    public async Task Handle_actualiza_solo_los_campos_enviados_conservando_identidad_estado_y_eventos(string field, bool active)
    {
        using var cancellation = new CancellationTokenSource();
        var provider = CreateProvider(active);
        _repository.GetByIdAsync(ProviderId, cancellation.Token).Returns(provider);
        _repository.ExistsByNameAsync(NewName, ProviderId, cancellation.Token).Returns(_ =>
        {
            // La unicidad se consulta con el agregado todavía intacto.
            AssertOriginal(provider, active);
            return false;
        });

        var result = await new UpdateInsuranceProviderCommandHandler(_repository)
            .HandleAsync(new UpdateInsuranceProviderCommand(ProviderId, ChangesFor(field)), cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(Unit.Value);
        result.Error.ShouldBe(Error.None);
        provider.Name.ShouldBe(Includes(field, "name") ? NewName : InsuranceProviderMother.DefaultName);
        provider.ContactPhone.ShouldBe(Includes(field, "phone") ? NewPhone : InsuranceProviderMother.DefaultContactPhone);
        provider.ContactEmail.Value.ShouldBe(Includes(field, "email") ? NewEmail : InsuranceProviderMother.DefaultContactEmail);
        provider.Address.ShouldBe(Includes(field, "address") ? NewAddress : InsuranceProviderMother.DefaultAddress);
        provider.Website.ShouldBe(Includes(field, "website") ? NewWebsite : InsuranceProviderMother.DefaultWebsite);
        AssertIdentityAndState(provider, active);
        await _repository.Received(1).GetByIdAsync(ProviderId, cancellation.Token);
        await _repository.Received(Includes(field, "name") ? 1 : 0).ExistsByNameAsync(NewName, ProviderId, cancellation.Token);
        _repository.DidNotReceive().Add(Arg.Any<InsuranceProvider>());
    }

    [Fact]
    public async Task Handle_recorta_los_extremos_y_normaliza_el_correo()
    {
        var provider = CreateProvider();
        _repository.GetByIdAsync(ProviderId, CancellationToken.None).Returns(provider);

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, new InsuranceProviderChanges(
                Set("  Seguros del Caribe\t"), Set(" 809-555-0999 "), Set(" Nuevo@Caribe.COM.do "),
                Set(" Calle Nueva 5, Santiago \r\n"), Set(" https://caribe.com.do \t"))),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(NewName);
        provider.ContactPhone.ShouldBe(NewPhone);
        provider.ContactEmail.Value.ShouldBe(NewEmail);
        provider.Address.ShouldBe(NewAddress);
        provider.Website.ShouldBe(NewWebsite);
        // La unicidad se comprueba con el nombre ya recortado, el mismo que se guarda.
        await _repository.Received(1).ExistsByNameAsync(NewName, ProviderId, CancellationToken.None);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \r\n\t ")]
    public async Task Handle_con_el_sitio_web_nulo_o_vacio_lo_quita_y_no_enviado_lo_conserva(string? website)
    {
        var withoutWebsite = CreateProvider();
        var untouched = CreateProvider();
        _repository.GetByIdAsync(ProviderId, CancellationToken.None).Returns(withoutWebsite, untouched);
        var handler = new UpdateInsuranceProviderCommandHandler(_repository);

        var removed = await handler.HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Website = Set(website) }), CancellationToken.None);
        var kept = await handler.HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { ContactPhone = Set(NewPhone) }), CancellationToken.None);

        removed.IsSuccess.ShouldBeTrue();
        withoutWebsite.Website.ShouldBeNull();
        withoutWebsite.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        kept.IsSuccess.ShouldBeTrue();
        untouched.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
        untouched.ContactPhone.ShouldBe(NewPhone);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    public static IEnumerable<object?[]> InvalidUpdates()
    {
        foreach (var value in MissingValues)
        {
            yield return ["name", value, "InsuranceProvider.NameRequired"];
            yield return ["phone", value, "InsuranceProvider.ContactPhoneRequired"];
            yield return ["email", value, "Common.InvalidEmail"];
            yield return ["address", value, "InsuranceProvider.AddressRequired"];
        }
        yield return ["name", new string('a', InsuranceProvider.MaxNameLength + 1), "InsuranceProvider.NameTooLong"];
        yield return ["phone", new string('1', InsuranceProvider.MaxContactPhoneLength + 1), "InsuranceProvider.ContactPhoneTooLong"];
        yield return ["email", "no-es-un-correo", "Common.InvalidEmail"];
        yield return ["address", new string('a', InsuranceProvider.MaxAddressLength + 1), "InsuranceProvider.AddressTooLong"];
        yield return ["website", BasePath + new string('a', InsuranceProvider.MaxWebsiteLength), "InsuranceProvider.WebsiteTooLong"];
        yield return ["website", "ftp://sitio.com", "InsuranceProvider.WebsiteInvalid"];
        yield return ["website", "javascript:alert(1)", "InsuranceProvider.WebsiteInvalid"];
        yield return ["none", null, "InsuranceProvider.NoFieldsProvided"];
    }

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Handle_rechaza_la_actualizacion_invalida_sin_mutar_ni_consultar_unicidad(
        string field, string? value, string expectedCode)
    {
        var provider = CreateProvider(false);
        _repository.GetByIdAsync(ProviderId, CancellationToken.None).Returns(provider);

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, Only(field, value)), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Type.ShouldBe(ErrorType.Validation);
        result.Error.Code.ShouldBe(expectedCode);
        AssertOriginal(provider, false);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<InsuranceProvider>());
    }

    [Fact]
    public async Task Handle_proveedor_inexistente_devuelve_NotFound_sin_consultar_unicidad()
    {
        using var cancellation = new CancellationTokenSource();

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Name = Set(NewName) }), cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(InsuranceProviderErrors.NotFound);
        result.Error.Code.ShouldBe("InsuranceProvider.NotFound");
        result.Error.Type.ShouldBe(ErrorType.NotFound);
        await _repository.Received(1).GetByIdAsync(ProviderId, cancellation.Token);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<InsuranceProvider>());
    }

    [Theory]
    [InlineData("Seguros Universal", "Seguros Universal")]
    [InlineData("  Seguros Universal  ", "Seguros Universal")]
    [InlineData("SEGUROS UNIVERSAL", "SEGUROS UNIVERSAL")]
    [InlineData(" seguros universal", "seguros universal")]
    public async Task Handle_permite_el_mismo_nombre_o_una_variante_de_mayusculas_y_excluye_el_id_propio(
        string name, string expected)
    {
        var provider = CreateProvider();
        _repository.GetByIdAsync(ProviderId, CancellationToken.None).Returns(provider);

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Name = Set(name) }), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(expected);
        provider.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        AssertIdentityAndState(provider, true);
        await _repository.Received(1).ExistsByNameAsync(expected, ProviderId, CancellationToken.None);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Handle_nombre_de_otro_proveedor_devuelve_NameAlreadyInUse_sin_mutar_el_agregado(bool active)
    {
        using var cancellation = new CancellationTokenSource();
        var provider = CreateProvider(active);
        _repository.GetByIdAsync(ProviderId, cancellation.Token).Returns(provider);
        _repository.ExistsByNameAsync(NewName, ProviderId, cancellation.Token).Returns(true);

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Name = Set("  Seguros del Caribe "), ContactPhone = Set(NewPhone) }),
            cancellation.Token);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldBe(InsuranceProviderErrors.NameAlreadyInUse);
        result.Error.Type.ShouldBe(ErrorType.Conflict);
        AssertOriginal(provider, active);
        await _repository.Received(1).ExistsByNameAsync(NewName, ProviderId, cancellation.Token);
    }

    [Fact]
    public async Task Handle_valida_antes_de_comprobar_el_conflicto_de_nombre()
    {
        var provider = CreateProvider();
        _repository.GetByIdAsync(ProviderId, CancellationToken.None).Returns(provider);
        _repository.ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Name = Set(NewName), ContactPhone = Set("  ") }),
            CancellationToken.None);

        result.Error.ShouldBe(InsuranceProviderErrors.ContactPhoneRequired);
        AssertOriginal(provider, true);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_conserva_exactamente_el_campo_omitido_aunque_tenga_espacios_heredados()
    {
        var provider = CreateProvider();
        const string legacyAddress = " \tAv.  Original 100 ";
        typeof(InsuranceProvider).GetProperty(nameof(InsuranceProvider.Address))!.SetValue(provider, legacyAddress);
        _repository.GetByIdAsync(ProviderId, CancellationToken.None).Returns(provider);

        var result = await new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Name = Set(NewName) }), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        provider.Name.ShouldBe(NewName);
        provider.Address.ShouldBe(legacyAddress);
        AssertIdentityAndState(provider, true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Handle_propaga_cancelacion_y_errores_sin_mutar(bool duringUniqueness, bool canceled)
    {
        using var cancellation = new CancellationTokenSource();
        var provider = CreateProvider();
        Exception original = canceled
            ? new OperationCanceledException(cancellation.Token)
            : new InvalidOperationException("Fallo de prueba.");
        if (canceled)
        {
            cancellation.Cancel();
        }
        _repository.GetByIdAsync(ProviderId, cancellation.Token)
            .Returns(_ => duringUniqueness
                ? Task.FromResult<InsuranceProvider?>(provider)
                : Task.FromException<InsuranceProvider?>(original));
        _repository.ExistsByNameAsync(NewName, ProviderId, cancellation.Token).Returns(Task.FromException<bool>(original));

        var actual = await Record.ExceptionAsync(() => new UpdateInsuranceProviderCommandHandler(_repository).HandleAsync(
            new(ProviderId, default(InsuranceProviderChanges) with { Name = Set(NewName) }), cancellation.Token));

        actual.ShouldBeSameAs(original);
        AssertOriginal(provider, true);
        await _repository.Received(1).GetByIdAsync(ProviderId, cancellation.Token);
        await _repository.Received(duringUniqueness ? 1 : 0).ExistsByNameAsync(NewName, ProviderId, cancellation.Token);
        _repository.DidNotReceive().Add(Arg.Any<InsuranceProvider>());
    }

    private static bool Includes(string field, string target) => field == "all" || field == target;

    // Valores con espacios y mayúsculas para comprobar la normalización de cada campo enviado.
    private static InsuranceProviderChanges ChangesFor(string field) => new(
        Includes(field, "name") ? Set(" \tSeguros del Caribe  ") : default,
        Includes(field, "phone") ? Set(" 809-555-0999 ") : default,
        Includes(field, "email") ? Set(" Nuevo@Caribe.COM.do ") : default,
        Includes(field, "address") ? Set("  Calle Nueva 5, Santiago \r\n") : default,
        Includes(field, "website") ? Set(" https://caribe.com.do \t") : default);

    private static InsuranceProviderChanges Only(string field, string? value) => field switch
    {
        "name" => default(InsuranceProviderChanges) with { Name = Set(value) },
        "phone" => default(InsuranceProviderChanges) with { ContactPhone = Set(value) },
        "email" => default(InsuranceProviderChanges) with { ContactEmail = Set(value) },
        "address" => default(InsuranceProviderChanges) with { Address = Set(value) },
        "website" => default(InsuranceProviderChanges) with { Website = Set(value) },
        "none" => default,
        _ => throw new ArgumentOutOfRangeException(nameof(field))
    };

    private InsuranceProvider CreateProvider(bool active = true)
    {
        var provider = InsuranceProviderMother.Valid();
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(provider, ProviderId);
        if (!active)
        {
            provider.Deactivate().IsSuccess.ShouldBeTrue();
        }
        typeof(AggregateRoot).GetMethod("Raise", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(provider, [_pendingEvent]);
        return provider;
    }

    private void AssertOriginal(InsuranceProvider provider, bool active)
    {
        provider.Name.ShouldBe(InsuranceProviderMother.DefaultName);
        provider.ContactPhone.ShouldBe(InsuranceProviderMother.DefaultContactPhone);
        provider.ContactEmail.Value.ShouldBe(InsuranceProviderMother.DefaultContactEmail);
        provider.Address.ShouldBe(InsuranceProviderMother.DefaultAddress);
        provider.Website.ShouldBe(InsuranceProviderMother.DefaultWebsite);
        AssertIdentityAndState(provider, active);
    }

    private void AssertIdentityAndState(InsuranceProvider provider, bool active)
    {
        provider.Id.ShouldBe(ProviderId);
        provider.IsActive.ShouldBe(active);
        provider.DomainEvents.Count.ShouldBe(1);
        provider.DomainEvents.Single().ShouldBeSameAs(_pendingEvent);
    }
}
