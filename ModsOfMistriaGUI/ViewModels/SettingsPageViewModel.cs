using System.Diagnostics;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI.Models;

namespace Garethp.ModsOfMistriaGUI.ViewModels;

public partial class SettingsPageViewModel : PageViewBase
{
    private const string NexusApiKeysUrl = "https://www.nexusmods.com/settings/api-keys";
    private readonly ModlistPageViewModel _modlist;
    private readonly Action _back;

    [ObservableProperty] private Settings _settings;
    [ObservableProperty] private string _selectedSection = "General";
    [ObservableProperty] private string _nexusApiKeyDraft = "";
    [ObservableProperty] private string _testStatus = "";

    public IReadOnlyList<string> Sections { get; } = ["General", "Nexus", "Test"];
    public bool IsGeneralSelected => SelectedSection == "General";
    public bool IsNexusSelected => SelectedSection == "Nexus";
    public bool IsTestSelected => SelectedSection == "Test";
    public bool IsNexusDistribution => AppInfo.IsNexusDistribution;

    public SettingsPageViewModel(Settings settings, ModlistPageViewModel modlist, Action back)
    {
        _settings = settings;
        _modlist = modlist;
        _back = back;
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneralSelected));
        OnPropertyChanged(nameof(IsNexusSelected));
        OnPropertyChanged(nameof(IsTestSelected));
    }

    [RelayCommand]
    private void Back() => _back();

    [RelayCommand]
    private async Task SelectModsLocation()
    {
        var topLevel = App.TopLevel;
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select mods folder",
            AllowMultiple = false
        });

        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
            Settings.ModsLocation = Path.GetFullPath(path);
    }

    [RelayCommand]
    private async Task OpenNexusApiKeys()
    {
        try
        {
            var topLevel = App.TopLevel;
            if (topLevel is not null)
                await topLevel.Launcher.LaunchUriAsync(new Uri(NexusApiKeysUrl));
        }
        catch
        {
            // A missing browser handler must not affect the installer.
        }
    }

    [RelayCommand]
    private void SimulateUpdates()
    {
        if (!IsNexusDistribution) return;

        foreach (var mod in _modlist.Mods)
        {
            mod.LatestVersion = $"{mod.Mod.GetVersion()} (test)";
            mod.UpdateDownloadUrl = mod.Mod.GetDownloadUrl();
            mod.UpdateAvailable = true;
        }

        TestStatus = $"Test update state applied to {_modlist.Mods.Count} mods. No network request was made.";
    }
}
