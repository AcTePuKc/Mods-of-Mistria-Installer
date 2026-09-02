using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.ViewModels;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace ModsOfMistriaGUITests;

public sealed class NexusOAuthViewModelTest
{
    private string _settingsDirectory = "";

    [SetUp]
    public void SetUp()
    {
        _settingsDirectory = Path.Combine(Path.GetTempPath(), $"aim-gui-oauth-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_settingsDirectory);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_settingsDirectory)) Directory.Delete(_settingsDirectory, true);
    }

    [Test]
    public async Task ShouldReportOAuthSignInTimeoutAndCancelTheLoopbackWait()
    {
        var nexusSettings = new NexusSettings(_settingsDirectory);
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var oauth = new NexusOAuthService(nexusSettings, registration);
        var viewModel = new NexusDownloadsViewModel(new Settings(), nexusSettings, oauth);
        string? messageTitle = null;
        string? message = null;
        var browserOpened = false;
        var stopwatch = Stopwatch.StartNew();

        var result = await viewModel.EnsureNexusAccountAsync(
            _ =>
            {
                browserOpened = true;
                return Task.CompletedTask;
            },
            (title, text) =>
            {
                messageTitle = title;
                message = text;
                return Task.CompletedTask;
            },
            TimeSpan.FromMilliseconds(100));

        stopwatch.Stop();

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.False);
            Assert.That(browserOpened, Is.True);
            Assert.That(messageTitle, Is.Not.Empty);
            Assert.That(message, Does.Contain("timed out"));
            Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(2)));
        });
    }

    [Test]
    public void ShouldExposeTheCorrectNexusAccountActionAndStatus()
    {
        var nexusSettings = new NexusSettings(_settingsDirectory);
        var registration = new NexusOAuthRegistration("aim-test-client-id", NewLoopbackRedirectUri());
        var oauth = new NexusOAuthService(nexusSettings, registration);
        var viewModel = new NexusDownloadsViewModel(new Settings(), nexusSettings, oauth);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NexusAccountActionText, Is.EqualTo("Sign in to Nexus"));
            Assert.That(viewModel.NexusAccountStatusText,
                Is.EqualTo("Nexus account not connected. Sign in to use Nexus features."));
        });

        nexusSettings.SetOAuthTokens(new NexusOAuthTokens(
            "access-token", "refresh-token", DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.NexusAccountActionText, Is.EqualTo("Sign out of Nexus"));
            Assert.That(viewModel.NexusAccountStatusText, Is.EqualTo("Nexus account connected."));
        });
    }

    private static Uri NewLoopbackRedirectUri()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new Uri($"http://127.0.0.1:{port}/callback");
    }
}
