using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaInstallerLibTests;

[TestFixture]
public class GameExecutableLocatorTest
{
    private string _root = "";

    [SetUp]
    public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "momi_game_executable_" + Path.GetRandomFileName());

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [TestCase("FieldsOfMistria.exe")]
    [TestCase("FieldsOfMistria")]
    public void FindsGameBinaryForWindowsAndNativeLinuxLayouts(string fileName)
    {
        Directory.CreateDirectory(_root);
        var binary = Path.Combine(_root, fileName);
        File.WriteAllText(binary, "fixture");

        Assert.That(GameExecutableLocator.Find(_root), Is.EqualTo(binary));
    }
}
