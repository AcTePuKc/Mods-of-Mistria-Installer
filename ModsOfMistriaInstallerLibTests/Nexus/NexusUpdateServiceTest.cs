using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Version comparison for update checks. Mod versions are written by hand, so this has to cope with
// more than well-formed semver.
[TestFixture]
public class NexusUpdateServiceTest
{
    [TestCase("1.0", "1.1", ExpectedResult = true, TestName = "a higher minor")]
    [TestCase("1.9", "1.10", ExpectedResult = true, TestName = "ten sorts above nine")]
    [TestCase("1.2.3", "1.2.4", ExpectedResult = true, TestName = "a higher patch")]
    [TestCase("v1.0", "v1.1", ExpectedResult = true, TestName = "a leading v")]
    [TestCase("1.2", "1.2", ExpectedResult = false, TestName = "the same version")]
    [TestCase("2.0", "1.9", ExpectedResult = false, TestName = "an older release")]
    [TestCase("1.2", "1.2.0", ExpectedResult = false, TestName = "a trailing zero")]
    [TestCase("1.0", "", ExpectedResult = false, TestName = "no candidate version")]
    [TestCase("", "1.0", ExpectedResult = true, TestName = "nothing installed to compare")]
    [TestCase("alpha", "beta", ExpectedResult = true, TestName = "unparseable versions that differ")]
    [TestCase("alpha", "alpha", ExpectedResult = false, TestName = "unparseable versions that match")]
    public bool ShouldCompareVersions(string installed, string candidate) =>
        NexusUpdateService.IsVersionNewer(installed, candidate);
}
