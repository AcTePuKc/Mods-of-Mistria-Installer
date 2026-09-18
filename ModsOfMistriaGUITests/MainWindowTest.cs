using Avalonia.Headless.NUnit;
using Garethp.ModsOfMistriaGUI;
using Garethp.ModsOfMistriaGUI.ViewModels;
using Garethp.ModsOfMistriaGUI.Views;
using Garethp.ModsOfMistriaGUI.Services;

namespace ModsOfMistriaGUITests;

public class Tests
{
    [AvaloniaTest]
    public void Should_Type_Text_Into_TextBox()
    {
        // Keep the title assertion independent of the machine's system locale
        // and any persisted UI-language preference.
        LocalizationService.Instance.SetLanguage("en");
        var mainViewModel = new MainWindowViewModel();

        // Setup controls:
        var window = new MainWindow() {DataContext = mainViewModel};

        // Open window:
        window.Show();

        Assert.That(window.Title, Is.EqualTo($"AIM - Alternative Installer for Mistria - {AppInfo.DisplayVersion}"));
    }

    [AvaloniaTest]
    public void Should_Localize_Available_Update_Message_When_Language_Changes()
    {
        LocalizationService.Instance.SetLanguage("en");
        var mainViewModel = new MainWindowViewModel();
        mainViewModel.ShowUpdateAvailable("0.15.8");

        LocalizationService.Instance.SetLanguage("bg");

        Assert.That(mainViewModel.UpdateMessage, Is.EqualTo("Налична е нова версия на AIM: 0.15.8."));
        LocalizationService.Instance.SetLanguage("en");
    }

    [Test]
    public void Should_Use_The_Complete_Polish_Resource_Set()
    {
        LocalizationService.Instance.SetLanguage("pl");

        Assert.Multiple(() =>
        {
            Assert.That(LocalizationService.Instance["GUIInstallButtonText"], Is.EqualTo("Instaluj"));
            Assert.That(LocalizationService.Instance["CoreCosmeticUiSubCategoryWrong"],
                Does.StartWith("Kosmetyk {0} ma nieprawidłowe ui_sub_category."));
        });

        LocalizationService.Instance.SetLanguage("en");
    }

    [Test]
    public void Should_Not_Broadcast_When_Selecting_The_Active_Language()
    {
        LocalizationService.Instance.SetLanguage("en");
        var notifications = 0;
        EventHandler handler = (_, _) => notifications++;
        LocalizationService.Instance.LanguageChanged += handler;

        try
        {
            LocalizationService.Instance.SetLanguage("en");
            Assert.That(notifications, Is.Zero);
        }
        finally
        {
            LocalizationService.Instance.LanguageChanged -= handler;
        }
    }

    [Test]
    public void Should_Only_Use_Aim_Branded_Releases_For_App_Updates()
    {
        Assert.Multiple(() =>
        {
            Assert.That(AppInfo.IsAimRelease("AIM 0.2.1"), Is.True);
            Assert.That(AppInfo.IsAimRelease("aim 0.2.2"), Is.True);
            Assert.That(AppInfo.IsAimRelease("MOMI 0.15.6 AI"), Is.False);
            Assert.That(AppInfo.IsAimRelease(null), Is.False);
        });
    }

    [TestCase("v0.3.0-rc.1", "0.3.0")]
    [TestCase("0.2.1", "0.2.1")]
    [TestCase("v0.3.0-preview+build.5", "0.3.0")]
    public void Should_Parse_The_Numeric_Core_Of_Aim_Release_Tags(string tag, string expectedVersion)
    {
        Assert.That(AppInfo.TryParseReleaseVersion(tag, out var version), Is.True);
        Assert.That(version, Is.EqualTo(Version.Parse(expectedVersion)));
    }

    [TestCase(null)]
    [TestCase("v0.3")]
    [TestCase("AIM 0.3.0")]
    public void Should_Reject_Invalid_Aim_Release_Tags(string? tag)
    {
        Assert.That(AppInfo.TryParseReleaseVersion(tag, out _), Is.False);
    }
}
