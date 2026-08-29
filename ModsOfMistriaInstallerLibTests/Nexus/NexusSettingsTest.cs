using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Newtonsoft.Json.Linq;

namespace ModsOfMistriaInstallerLibTests.Nexus;

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
    public void ShouldRoundTripOAuthTokens()
    {
        var tokens = new NexusOAuthTokens("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1));
        new NexusSettings(_directory).SetOAuthTokens(tokens);

        Assert.Multiple(() =>
        {
            Assert.That(new NexusSettings(_directory).GetOAuthTokens(), Is.EqualTo(tokens));
            Assert.That(new NexusSettings(_directory).HasOAuthTokens(), Is.True);
        });
    }

    [Test]
    public void ShouldNotWriteOAuthTokensOutInPlainTextOnWindows()
    {
        if (!OperatingSystem.IsWindows()) Assert.Ignore("DPAPI protection only applies on Windows");

        new NexusSettings(_directory).SetOAuthTokens(
            new NexusOAuthTokens("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1)));

        var contents = File.ReadAllText(Path.Combine(_directory, "nexus.json"));
        Assert.That(contents, Does.Not.Contain("access-token").And.Not.Contain("refresh-token"));
    }

    [Test]
    public void ShouldPermanentlyRemoveLegacyPersonalApiKeys()
    {
        File.WriteAllText(Path.Combine(_directory, "nexus.json"), new JObject
        {
            ["nexusApiKey"] = "legacy-personal-key",
            ["nexusApiKeyProtected"] = "legacy-protected-key",
            ["nxmHandlerRegistered"] = true
        }.ToString());

        var settings = new NexusSettings(_directory);
        var contents = File.ReadAllText(Path.Combine(_directory, "nexus.json"));

        Assert.Multiple(() =>
        {
            Assert.That(settings.HasOAuthTokens(), Is.False);
            Assert.That(settings.HandlerRegistered, Is.True);
            Assert.That(contents, Does.Not.Contain("legacy-personal-key").And.Not.Contain("legacy-protected-key"));
            Assert.That(contents, Does.Not.Contain("nexusApiKey"));
        });
    }

    [Test]
    public void ShouldForgetTokensWhenDisconnected()
    {
        var settings = new NexusSettings(_directory);
        settings.SetOAuthTokens(new NexusOAuthTokens("access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1)));
        settings.SetOAuthTokens(null);

        Assert.Multiple(() =>
        {
            Assert.That(settings.GetOAuthTokens(), Is.Null);
            Assert.That(new NexusSettings(_directory).HasOAuthTokens(), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(_directory, "nexus.json")),
                Does.Not.Contain("access-token").And.Not.Contain("refresh-token"));
        });
    }

    [Test]
    public void ShouldRememberTheProtocolOptIn()
    {
        new NexusSettings(_directory).HandlerRegistered = true;
        Assert.That(new NexusSettings(_directory).HandlerRegistered, Is.True);
    }

    [Test]
    public void ShouldRememberTheStandingProtocolClaim()
    {
        new NexusSettings(_directory).HandlerAlwaysClaim = true;
        Assert.That(new NexusSettings(_directory).HandlerAlwaysClaim, Is.True);
    }

    [Test]
    public void ShouldRememberWhichHandlerWasPromptedAbout()
    {
        new NexusSettings(_directory).HandlerPromptedFor = "Vortex";
        Assert.That(new NexusSettings(_directory).HandlerPromptedFor, Is.EqualTo("Vortex"));
    }

    [Test]
    public void ShouldTreatMissingPromptedHandlerAsNoPreviousClaimant()
    {
        Assert.That(new NexusSettings(_directory).HandlerPromptedFor, Is.Null);
    }
}
