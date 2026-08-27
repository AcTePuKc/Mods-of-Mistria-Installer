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
    private string _selectedSectionId = "general";

    public IReadOnlyList<string> Sections => [Texts.GUISettingsGeneral, Texts.GUISettingsNexus];
    public string SelectedSection
    {
        get => _selectedSectionId == "nexus" ? Texts.GUISettingsNexus : Texts.GUISettingsGeneral;
        set
        {
            var newId = value == Texts.GUISettingsNexus ? "nexus" : "general";
            if (_selectedSectionId == newId) return;
            _selectedSectionId = newId;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsNexusSelected));
        }
    }

    public bool IsGeneralSelected => _selectedSectionId == "general";
    public bool IsNexusSelected => _selectedSectionId == "nexus";
    public NexusDownloadsViewModel Nexus => _nexus;
    public string NexusAccountStatus => Nexus.IsNexusAccountConnected
        ? "Nexus account connected."
        : "Nexus account connection is awaiting OAuth registration.";

    public SettingsPageViewModel(Settings settings, NexusDownloadsViewModel nexus, Action back)
    {
        _settings = settings;
        _nexus = nexus;
        _back = back;
        Texts.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(SelectedSection));
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsNexusSelected));
            OnPropertyChanged(nameof(NexusAccountStatus));
        };
        _nexus.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NexusDownloadsViewModel.IsNexusAccountConnected))
                OnPropertyChanged(nameof(NexusAccountStatus));
        };
    }

    [RelayCommand]
    private void Back() => _back();

    [RelayCommand]
    private async Task ManageNexusAccount() => await _nexus.ManageNexusAccountAsync();

    [RelayCommand]
    private async Task SelectModsLocation()
    {
        var topLevel = App.TopLevel;
        if (topLevel is null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Texts.GUISettingsSelectModsFolderTitle,
            AllowMultiple = false
        });

        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path)
            Settings.ModsLocation = Path.GetFullPath(path);
    }

}
