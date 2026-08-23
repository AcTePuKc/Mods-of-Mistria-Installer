using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaInstallerLibTests.Nexus;

// Naming whichever program currently owns nxm:// links. The OS hands back a whole command line, and
// showing that raw is how a menu ends up reading 'Another program handles these links ("C:\Program F'.
[TestFixture]
public class NxmHandlerStatusTest
{
    private static string? NameOf(string? handler) =>
        new NxmHandlerStatus(true, false, handler).HandlerName;

    [TestCase("\"C:\\Program Files\\Black Tree Gaming Ltd\\Vortex\\Vortex.exe\" -d \"%1\"", "Vortex",
        TestName = "a quoted Windows command line")]
    [TestCase("\"C:\\Games\\MO2\\nxmhandler.exe\" \"%1\"", "nxmhandler",
        TestName = "Mod Organizer's handler")]
    [TestCase("C:\\tools\\Handler.exe %1", "Handler",
        TestName = "an unquoted path")]
    [TestCase("aim-nxm-handler.desktop", "aim-nxm-handler",
        TestName = "a Linux desktop entry")]
    public void ShouldNameTheProgramBehindTheRegistration(string handler, string expected)
    {
        Assert.That(NameOf(handler), Is.EqualTo(expected));
    }

    [Test]
    public void ShouldFallBackToTheRawCommandWhenThereIsNoFileName()
    {
        Assert.That(NameOf("\""), Is.EqualTo("\""));
    }

    [Test]
    public void ShouldHaveNoNameWhenNothingIsRegistered()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NameOf(null), Is.Null);
            Assert.That(NameOf("   "), Is.Null);
            Assert.That(new NxmHandlerStatus(false, false, null).IsClaimedByAnother, Is.False);
            Assert.That(new NxmHandlerStatus(true, false, "other.exe").IsClaimedByAnother, Is.True);
            Assert.That(new NxmHandlerStatus(true, true, "aim.exe").IsClaimedByAnother, Is.False);
        });
    }
}
