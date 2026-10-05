using Microsoft.Extensions.Options;
using SGCM.Data.Application.Common;

namespace SGCM.Data.Infrastructure.Persistence;

public sealed class DatabaseOptions
{
    public const string SectionName = "ConnectionStrings";

    public string? SgcmDatabase { get; set; }
}

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{   
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        var errors = new List<string>();

        OptionsRules.Required(errors, "ConnectionStrings:SgcmDatabase", options.SgcmDatabase);

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}