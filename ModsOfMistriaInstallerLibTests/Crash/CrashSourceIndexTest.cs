using Garethp.ModsOfMistriaInstallerLib.Crash;

namespace ModsOfMistriaInstallerLibTests.Crash;

[TestFixture]
public class CrashSourceIndexTest
{
    private string _gameFolder = "";

    [SetUp]
    public void SetUp()
    {
        _gameFolder = Path.Combine(Path.GetTempPath(), $"aim-crash-source-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_gameFolder, "assets", "gml"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_gameFolder)) Directory.Delete(_gameFolder, true);
    }

    [Test]
    public void ReadsAFileInsideTheGameDirectory()
    {
        var path = Path.Combine(_gameFolder, "assets", "gml", "source.gml");
        File.WriteAllText(path, "function source() {\n    return 1;\n}");

        using var index = CrashSourceIndex.Open(_gameFolder);

        Assert.That(index.ReadAll("assets/gml/source.gml"), Is.EqualTo(
            new[] { "function source() {", "    return 1;", "}" }));
    }

    [Test]
    public void DoesNotReadOutsideTheGameDirectoryThroughTraversal()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_gameFolder)!, "aim-crash-source-secret.txt");
        File.WriteAllText(outside, "must not be disclosed");

        try
        {
            using var index = CrashSourceIndex.Open(_gameFolder);

            Assert.That(index.ReadAll("../aim-crash-source-secret.txt"), Is.Null);
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }

    [Test]
    public void DoesNotReadAWindowsRootedPath()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"aim-crash-source-rooted-{Guid.NewGuid():N}.txt");
        File.WriteAllText(outside, "must not be disclosed");

        try
        {
            using var index = CrashSourceIndex.Open(_gameFolder);

            Assert.That(index.ReadAll(outside), Is.Null);
        }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }
    }
}
