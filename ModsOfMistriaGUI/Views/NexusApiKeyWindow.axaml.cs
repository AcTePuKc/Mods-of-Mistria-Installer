using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// Collects the user's personal Nexus API key. The key is checked against the API before it is
/// saved, so a mistyped or revoked key is caught here rather than at the first download.
/// </summary>
public partial class NexusApiKeyWindow : Window
{
    private const string AccountApiPage = "https://www.nexusmods.com/users/myaccount?tab=api";

    // One client for the lifetime of the app: a new HttpClient per key check would leak a socket
    // handle every time the user pressed Save.
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly NexusSettings _settings;
    private bool _busy;

    // Parameterless constructor for the XAML designer only.
    public NexusApiKeyWindow() : this(new NexusSettings())
    {
    }

    public NexusApiKeyWindow(NexusSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        Title = Text("GUINexusApiKeyTitle");
        HeadingText.Text = Text("GUINexusApiKeyTitle");
        ExplanationText.Text = Text("GUINexusApiKeyExplanation");
        OpenAccountPageButton.Content = Text("GUINexusOpenAccountPage");
        ApiKeyBox.Watermark = Text("GUINexusApiKeyWatermark");
        RemoveButton.Content = Text("GUINexusRemoveApiKey");
        CancelButton.Content = Text("GUICancel");
        SaveButton.Content = Text("GUISave");

        RemoveButton.IsVisible = _settings.HasApiKey();
        if (_settings.HasApiKey()) StatusText.Text = Text("GUINexusApiKeyAlreadySet");
    }

    /// <summary>Returns true when a working key was saved.</summary>
    public static async Task<bool> ShowAsync(Window owner, NexusSettings settings) =>
        await new NexusApiKeyWindow(settings).ShowDialog<bool>(owner);

    private static string Text(string key) => LocalizationService.Instance[key];

    private void OnOpenAccountPage(object? sender, RoutedEventArgs e)
    {
        if (!ExternalUrl.IsAllowed(AccountApiPage)) return;

        Process.Start(new ProcessStartInfo { FileName = AccountApiPage, UseShellExecute = true });
    }

    private async void OnKeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SaveAsync();
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnRemove(object? sender, RoutedEventArgs e)
    {
        _settings.SetApiKey(null);
        Close(false);
    }

    private async Task SaveAsync()
    {
        if (_busy) return;

        var apiKey = ApiKeyBox.Text?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            StatusText.Text = Text("GUINexusApiKeyMissing");
            return;
        }

        _busy = true;
        SaveButton.IsEnabled = false;
        StatusText.Text = Text("GUINexusApiKeyChecking");

        try
        {
            var user = await new NexusApiClient(apiKey, Http).ValidateKeyAsync();
            _settings.SetApiKey(apiKey);

            StatusText.Text = string.Format(Text("GUINexusApiKeyAccepted"), user.Name);
            Close(true);
        }
        catch (Exception exception)
        {
            StatusText.Text = exception.Message;
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
        }
    }
}
