using SGCM.Data.Infrastructure.Notifications.Channels;
using Shouldly;

namespace SGCM.Test.Unit.Infrastructure;

public class SmtpOptionsValidatorTests
{
    private readonly SmtpOptionsValidator _validator = new();

    private static SmtpOptions ValidOptions() => new()
    {
        Host = "localhost",
        Port = 54325,
        SenderEmail = "no-reply@sgcm.local",
        SenderName = "SGCM"
    };

    [Fact]
    public void Configuracion_valida_pasa()
    {
        _validator.Validate(null, ValidOptions()).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public void Sin_host_falla_y_nombra_la_variable_de_entorno()
    {
        var options = ValidOptions();
        options.Host = "";

        var result = _validator.Validate(null, options);

        result.Failed.ShouldBeTrue();
        result.FailureMessage.ShouldContain("Smtp__Host");
    }

    [Fact]
    public void Puerto_cero_falla()
    {
        var options = ValidOptions();
        options.Port = 0;

        _validator.Validate(null, options).Failed.ShouldBeTrue();
    }
}