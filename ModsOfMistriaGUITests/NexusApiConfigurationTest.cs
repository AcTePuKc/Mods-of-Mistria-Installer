using Garethp.ModsOfMistriaGUI.Models;

namespace Garethp.ModsOfMistriaGUITests;

public sealed class NexusApiConfigurationTest
{
    [Test]
    public void DisabledConfigurationContainsNoCredentials()
    {
        var configuration = NexusApiConfiguration.Disabled;

        Assert.That(configuration.Enabled, Is.False);
        Assert.That(configuration.ApplicationSlug, Is.Empty);
        Assert.That(configuration.IsConfigured, Is.False);
    }
}
