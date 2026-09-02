using Garethp.ModsOfMistriaInstallerLib;

namespace ModsOfMistriaInstallerLibTests;

[TestFixture]
public class InstallerVersionTest
{
    [Test]
    public void SupportsTheLatestUpstreamMomiManifestVersion()
    {
        Assert.That(InstallerVersion.ModCompatibilityVersion, Is.EqualTo(new Version(0, 15, 10)));
    }
}
