using Garethp.ModsOfMistriaInstallerLib.Utils;
using Tomlyn.Model;
using Tomlyn.Serialization;

namespace Garethp.ModsOfMistriaInstallerLib.Models.SDK;

public class MistMetaFile
{
    [TomlPropertyName("meta_properties")]
    public MetaProperties Meta { get; set; } = new()
    {
        Id = IDManager.GenerateUniqueId(),
        AssetKind = "Mist"
    };

    [TomlPropertyName("asset_properties")]
    public TomlTable Asset { get; set; } = new();
}
