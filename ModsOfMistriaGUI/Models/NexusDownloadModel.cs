using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaGUI.Models;

/// <summary>
/// One row in the downloads strip: a single mod being fetched from Nexus, from the moment the
/// link arrives until it is installed, failed or cancelled.
/// </summary>
public partial class NexusDownloadModel : ObservableObject
{
    private readonly CancellationTokenSource _cancellation = new();

    public NexusDownloadModel(string title)
    {
        _title = title;
    }

    [ObservableProperty] private string _title;

    [ObservableProperty] private string _status = "";

    /// <summary>Percentage, for the progress bar.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>True until the server tells us how large the file is.</summary>
    [ObservableProperty] private bool _isIndeterminate = true;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCancel))]
    private bool _isFinished;

    [ObservableProperty] private bool _isFailed;

    public bool CanCancel => !IsFinished;

    public CancellationToken Token => _cancellation.Token;

    [RelayCommand]
    private void Cancel()
    {
        if (IsFinished) return;

        Status = "Cancelling...";
        _cancellation.Cancel();
    }

    public void Apply(NxmDownloadProgress progress)
    {
        Status = progress.Message;

        var fraction = progress.Fraction;
        IsIndeterminate = progress.Stage switch
        {
            NxmDownloadStage.Downloading => fraction is null,
            NxmDownloadStage.Resolving or NxmDownloadStage.Installing or NxmDownloadStage.Queued => true,
            _ => false
        };

        if (fraction is not null) Progress = fraction.Value * 100;

        switch (progress.Stage)
        {
            case NxmDownloadStage.Completed:
                Progress = 100;
                IsFinished = true;
                break;
            case NxmDownloadStage.Failed:
                IsFailed = true;
                IsFinished = true;
                break;
            case NxmDownloadStage.Cancelled:
                IsFinished = true;
                break;
        }
    }
}
