using Garethp.ModsOfMistriaInstallerLib.Operations;
using Tomlyn;
using Tomlyn.Model;

namespace ModsOfMistriaInstallerLibTests.Operations;

[TestFixture]
public class MOMIOperationsTest
{
    [Test]
    public void TableMergeAppendsNestedInlineArrays()
    {
        var destination = TomlSerializer.Deserialize<TomlTable>("""
            [common]
            small_roll = [{ purse = 200 }, { item = "apple" }]
            """)!;
        var source = TomlSerializer.Deserialize<TomlTable>("""
            [common]
            small_roll = [{ pet_cosmetic = "skull_mask" }]
            MOMIaction = "merge"
            """)!;

        MOMIOperations.MergeTomlTables(destination, source);

        var common = (TomlTable)destination["common"];
        var rolls = (TomlArray)common["small_roll"];
        Assert.That(rolls, Has.Count.EqualTo(3));
        Assert.That(((TomlTable)rolls[0]!) ["purse"], Is.EqualTo(200L));
        Assert.That(((TomlTable)rolls[2]!) ["pet_cosmetic"], Is.EqualTo("skull_mask"));
    }

    [Test]
    public void ArrayValuesAreClonedWhenAdded()
    {
        var destination = new TomlTable
        {
            ["items"] = new TomlArray { "existing" }
        };
        var source = new TomlTable
        {
            ["items"] = new TomlArray { "added" }
        };

        MOMIOperations.MergeTomlTables(destination, source, mergeArrays: true);
        ((TomlArray)destination["items"]!).Add("destination-only");

        Assert.That((TomlArray)source["items"]!, Has.Count.EqualTo(1));
        Assert.That((TomlArray)destination["items"]!, Has.Count.EqualTo(3));
    }
}
