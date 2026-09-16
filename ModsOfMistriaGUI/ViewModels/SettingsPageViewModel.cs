using Avalonia;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaGUI;
using Garethp.ModsOfMistriaGUI.Models;

namespace Garethp.ModsOfMistriaGUI.ViewModels;

public partial class SettingsPageViewModel : PageViewBase
{
    private readonly Action _back;
    private readonly NexusDownloadsViewModel _nexus;

    [ObservableProperty] private Settings _settings;
    [ObservableProperty] private bool _isCheckingForAimUpdates;
    [ObservableProperty] private string _aimUpdateCheckStatus = "";
    private string _selectedSectionId = "general";

    public IReadOnlyList<string> Sections => [Texts.GUISettingsGeneral, Texts.GUISettingsUpdates, Texts.GUISettingsNexus];
    public string SelectedSection
    {
        get => _selectedSectionId switch
        {
            "updates" => Texts.GUISettingsUpdates,
            "nexus" => Texts.GUISettingsNexus,
            _ => Texts.GUISettingsGeneral
        };
        set
        {
            var newId = value == Texts.GUISettingsUpdates ? "updates"
                : value == Texts.GUISettingsNexus ? "nexus"
                : "general";
            if (_selectedSectionId == newId) return;
            _selectedSectionId = newId;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsGeneralSelected));
            OnPropertyChanged(nameof(IsUpdatesSelected));
            OnPropertyChanged(nameof(IsNexusSelected));
        }
    }

    public bool IsGeneralSelected => _selectedSectionId == "general";
    public bool IsUpdatesSelected => _selectedSectionId == "updates";
    public bool IsNexusSelected => _selectedSectionId == "nexus";
    public NexusDownloadsViewModel Nexus => _nexus;
    public string NexusAccountStatus => Nexus.NexusAccountStatusText;

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
            OnPropertyChanged(nameof(IsUpdatesSelected));
            OnPropertyChanged(nameof(IsNexusSelected));
            OnPropertyChanged(nameof(NexusAccountStatus));
        };
        _nexus.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(NexusDownloadsViewModel.IsNexusAccountConnected)
                or nameof(NexusDownloadsViewModel.NexusAccountStatusText))
                OnPropertyChanged(nameof(NexusAccountStatus));
        };
    }

    [RelayCommand]
    private void Back() => _back();

    [RelayCommand]
    private async Task ManageNexusAccount() => await _nexus.ManageNexusAccountAsync();

    [RelayCommand]
    private async Task CheckForAimUpdates()
    {
        IsCheckingForAimUpdates = true;
        AimUpdateCheckStatus = Texts.GUISettingsCheckingForUpdates;
        try
        {
            var app = Application.Current as App;
            var result = app is null
                ? App.UpdateCheckResult.Failed
                : await app.CheckForUpdatesNowAsync();
            AimUpdateCheckStatus = result switch
            {
                App.UpdateCheckResult.Available => Texts.GUISettingsUpdateAvailable,
                App.UpdateCheckResult.UpToDate => Texts.GUISettingsUpToDate,
                _ => Texts.GUISettingsUpdateCheckFailed
            };
        }
        finally
        {
            IsCheckingForAimUpdates = false;
        }
    }

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
