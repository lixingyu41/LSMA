using System.Text.Json.Serialization;

namespace LSMA.Models;

public sealed class ModManifest
{
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? UniqueID { get; set; }
    public string? EntryDll { get; set; }
    public ContentPackReference? ContentPackFor { get; set; }
    public string? MinimumApiVersion { get; set; }
    public string? MinimumGameVersion { get; set; }
    public List<string> UpdateKeys { get; set; } = [];
    public List<ModDependency> Dependencies { get; set; } = [];
}

public sealed class ContentPackReference
{
    public string? UniqueID { get; set; }
    public string? MinimumVersion { get; set; }
}

public sealed class ModDependency
{
    public string? UniqueID { get; set; }
    public string? MinimumVersion { get; set; }

    [JsonPropertyName("IsRequired")]
    public bool IsRequired { get; set; } = true;
}
