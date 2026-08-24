using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// The Nexus API key and the protocol opt-in. Written to a temp directory so the suite never
// touches the key a developer has saved on their own machine.
[TestFixture]
public class NexusSettingsTest
{
    private string _directory = "";

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"aim-settings-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Test]
    public void ShouldRoundTripAnApiKey()
    {
        new NexusSettings(_directory).SetApiKey("  my-secret-key  ");

        Assert.Multiple(() =>
        {
            Assert.That(new NexusSettings(_directory).GetApiKey(), Is.EqualTo("my-secret-key"));
            Assert.That(new NexusSettings(_directory).HasApiKey(), Is.True);
        });
    }

    [Test]
    public void ShouldNotWriteTheKeyOutInPlainTextOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI protection only applies on Windows");

        new NexusSettings(_directory).SetApiKey("my-secret-key");

        var contents = File.ReadAllText(Path.Combine(_directory, "nexus.json"));
        Assert.That(contents, Does.Not.Contain("my-secret-key"));
    }

    [Test]
    public void ShouldForgetTheKeyWhenItIsCleared()
    {
        var settings = new NexusSettings(_directory);
        settings.SetApiKey("my-secret-key");
        settings.SetApiKey(null);

        Assert.Multiple(() =>
        {
            Assert.That(settings.GetApiKey(), Is.Null);
            Assert.That(new NexusSettings(_directory).HasApiKey(), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_directory, "nexus.json")),
                Does.Not.Contain("my-secret-key"));
        });
    }

    [Test]
    public void ShouldRememberTheProtocolOptIn()
    {
        new NexusSettings(_directory).HandlerRegistered = true;

        Assert.That(new NexusSettings(_directory).HandlerRegistered, Is.True);
    }

    [Test]
    public void ShouldStartFreshWhenTheFileIsUnreadable()
    {
        File.WriteAllText(Path.Combine(_directory, "nexus.json"), "{ this is not json");

        Assert.That(new NexusSettings(_directory).HasApiKey(), Is.False);
    }
}
