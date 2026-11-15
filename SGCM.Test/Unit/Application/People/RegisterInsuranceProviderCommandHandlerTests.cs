using NSubstitute;
using SGCM.Data.Application.Abstractions.Persistence;
using SGCM.Data.Application.People.Commands.RegisterInsuranceProvider;
using SGCM.Data.Domain.People.InsuranceProviders;
using Shouldly;

namespace SGCM.Test.Unit.Application.People;

public sealed class RegisterInsuranceProviderCommandHandlerTests
{
    private readonly IInsuranceProviderRepository _repository = Substitute.For<IInsuranceProviderRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();

    [Fact]
    public async Task Handle_recorta_y_guarda_el_proveedor_activo_respetando_el_token()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new RegisterInsuranceProviderCommandHandler(_repository, _unitOfWork);
        var command = new RegisterInsuranceProviderCommand(
            "  Seguros Universal  ", "  8095550200  ", "Contacto@Seguros.com", "  Av. Winston Churchill 100  ",
            "  https://www.segurosuniversal.com  ");

        var result = await handler.HandleAsync(command, cancellation.Token);

        result.IsSuccess.ShouldBeTrue();
        Received.InOrder(() =>
        {
            _repository.ExistsByNameAsync("Seguros Universal", null, cancellation.Token);
            _repository.Add(Arg.Is<InsuranceProvider>(value => value.Name == "Seguros Universal"
                && value.ContactPhone == "8095550200"
                && value.ContactEmail.Value == "contacto@seguros.com"
                && value.Address == "Av. Winston Churchill 100"
                && value.Website == "https://www.segurosuniversal.com"
                && value.IsActive));
            _unitOfWork.SaveChangesAsync(cancellation.Token);
        });
        await _unitOfWork.Received(1).SaveChangesAsync(cancellation.Token);
    }

    [Fact]
    public async Task Handle_nombre_duplicado_no_agrega_ni_guarda()
    {
        _repository.ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>()).Returns(true);
        var handler = new RegisterInsuranceProviderCommandHandler(_repository, _unitOfWork);

        var result = await handler.HandleAsync(ValidCommand(), CancellationToken.None);

        result.Error.ShouldBe(InsuranceProviderErrors.NameAlreadyInUse);
        _repository.DidNotReceive().Add(Arg.Any<InsuranceProvider>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_consulta_el_nombre_recortado_sin_excluir_ningun_proveedor()
    {
        var handler = new RegisterInsuranceProviderCommandHandler(_repository, _unitOfWork);

        await handler.HandleAsync(ValidCommand() with { Name = "  Seguros Universal  " }, CancellationToken.None);

        await _repository.Received(1).ExistsByNameAsync("Seguros Universal", null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("", "8095550200", "contacto@seguros.com", "Dirección", "InsuranceProvider.NameRequired")]
    [InlineData("Seguros Universal", "", "contacto@seguros.com", "Dirección", "InsuranceProvider.ContactPhoneRequired")]
    [InlineData("Seguros Universal", "8095550200", "sin-arroba", "Dirección", "Common.InvalidEmail")]
    [InlineData("Seguros Universal", "8095550200", "contacto@seguros.com", "   ", "InsuranceProvider.AddressRequired")]
    public async Task Handle_rechaza_datos_invalidos_sin_consultar_ni_guardar(
        string name, string contactPhone, string contactEmail, string address, string expectedCode)
    {
        var handler = new RegisterInsuranceProviderCommandHandler(_repository, _unitOfWork);

        var result = await handler.HandleAsync(
            new RegisterInsuranceProviderCommand(name, contactPhone, contactEmail, address, null), CancellationToken.None);

        result.Error.Code.ShouldBe(expectedCode);
        await _repository.DidNotReceive().ExistsByNameAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<InsuranceProvider>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    private static RegisterInsuranceProviderCommand ValidCommand() =>
        new("Seguros Universal", "8095550200", "contacto@seguros.com", "Av. Winston Churchill 100", null);
}
