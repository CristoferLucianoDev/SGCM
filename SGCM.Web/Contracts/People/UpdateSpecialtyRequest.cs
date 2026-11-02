using System.Text.Json.Serialization;

namespace SGCM.Web.Contracts.People;

public sealed class UpdateSpecialtyRequest
{
    private string? _name;
    private string? _description;

    public string? Name
    {
        get => _name;
        init
        {
            _name = value;
            NameProvided = true;
        }
    }

    public string? Description
    {
        get => _description;
        init
        {
            _description = value;
            DescriptionProvided = true;
        }
    }

    [JsonIgnore]
    public bool NameProvided { get; private set; }

    [JsonIgnore]
    public bool DescriptionProvided { get; private set; }
}
