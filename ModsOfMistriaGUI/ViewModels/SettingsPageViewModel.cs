using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI.Models;

namespace Garethp.ModsOfMistriaGUI.ViewModels;

public partial class SettingsPageViewModel : PageViewBase
{
    private readonly Action _back;
    private readonly NexusDownloadsViewModel _nexus;

    [ObservableProperty] private Settings _settings;
    [ObservableProperty] private string _selectedSection = "General";

    public IReadOnlyList<string> Sections { get; } = ["General", "Nexus"];
    public bool IsGeneralSelected => SelectedSection == "General";
    public bool IsNexusSelected => SelectedSection == "Nexus";
    public NexusDownloadsViewModel Nexus => _nexus;

    public SettingsPageViewModel(Settings settings, NexusDownloadsViewModel nexus, Action back)
    {
        _settings = settings;
        _nexus = nexus;
        _back = back;
    }

    partial void OnSelectedSectionChanged(string value)
    {
        OnPropertyChanged(nameof(IsGeneralSelected));
        OnPropertyChanged(nameof(IsNexusSelected));
    }

    [RelayCommand]
    private void Back() => _back();

    [RelayCommand]
    private async Task SetNexusApiKey() => await _nexus.SetApiKeyAsync();

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

}
