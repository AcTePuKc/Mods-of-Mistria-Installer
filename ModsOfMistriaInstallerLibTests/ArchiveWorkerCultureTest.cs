using System.Globalization;
using Garethp.ModsOfMistriaInstallerLib.Lang;
using Garethp.ModsOfMistriaInstallerLib.Worker;

namespace ModsOfMistriaInstallerLibTests;

[TestFixture]
[NonParallelizable]
public sealed class ArchiveWorkerCultureTest
{
    [Test]
    public void AppliesTheGuiCultureToWorkerResources()
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        var previousResourceCulture = Resources.Culture;
        try
        {
            ArchiveWorkerCulture.Apply("bg");

            Assert.That(Resources.Culture?.Name, Is.EqualTo("bg"));
            Assert.That(Resources.ResourceManager.GetString("GUISavingAtlases", Resources.Culture),
                Is.EqualTo("Записване на атласите"));
            Assert.That(Resources.CoreInstallSummary, Does.Not.Contain("mod(s) installed"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
            Resources.Culture = previousResourceCulture;
        }
    }

    [Test]
    public void KeepsTheNormalCultureForAnAbsentOrInvalidRequest()
    {
        var previous = Resources.Culture;
        try
        {
            Resources.Culture = CultureInfo.GetCultureInfo("en");
            ArchiveWorkerCulture.Apply("");
            ArchiveWorkerCulture.Apply("not-a-real-culture");

            Assert.That(Resources.Culture?.Name, Is.EqualTo("en"));
        }
        finally
        {
            Resources.Culture = previous;
        }
    }
}
