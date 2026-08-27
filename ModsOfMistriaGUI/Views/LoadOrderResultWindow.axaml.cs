using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;

namespace Garethp.ModsOfMistriaGUI.Views;

public partial class LoadOrderResultWindow : Window
{
    public sealed record ReportContent(string Summary, IReadOnlyList<LoadOrderNote> Notes);

    private string _report = string.Empty;
    private readonly Func<Task<ReportContent>>? _refreshReportAsync;

    // Required by Avalonia's compiled XAML loader; normal callers use ShowAsync below.
    public LoadOrderResultWindow() : this(string.Empty, [])
    {
    }

    private LoadOrderResultWindow(
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        string? title = null,
        bool showCopyButton = false,
        bool compact = false,
        Func<Task<ReportContent>>? refreshReportAsync = null)
    {
        InitializeComponent();

        Title = title ?? LocalizationService.Instance["GUILoadOrderTitle"];
        _refreshReportAsync = refreshReportAsync;
        ApplyReport(new ReportContent(summary, notes));
        CopyButton.IsVisible = showCopyButton;
        RefreshButton.IsVisible = refreshReportAsync is not null;
        CloseButton.IsVisible = !compact;
        if (compact)
        {
            SizeToContent = SizeToContent.Height;
            Width = 520;
            MinWidth = 420;
            MaxWidth = 720;
            MinHeight = 0;
            MaxHeight = 420;
        }
        RefreshButton.Content = LocalizationService.Instance["GUIRefreshReport"];
        CopyButton.Content = LocalizationService.Instance["GUICopyReport"];
        CloseButton.Content = LocalizationService.Instance["GUIClose"];
        CopyButton.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync(_report);
        };
        RefreshButton.Click += async (_, _) => await RefreshReportAsync();
        CloseButton.Click += (_, _) => Close();
    }

    public static Task ShowAsync(
        Window owner,
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        string? title = null,
        bool showCopyButton = false,
        bool compact = false)
    {
        return new LoadOrderResultWindow(summary, notes, title, showCopyButton, compact).ShowDialog(owner);
    }

    /// <summary>
    /// Opens a modeless issue report. The caller supplies a deliberate refresh
    /// action because scanning a large mod set should never run for every edit.
    /// </summary>
    public static LoadOrderResultWindow Show(
        ReportContent report,
        string title,
        Func<Task<ReportContent>> refreshReportAsync)
    {
        var window = new LoadOrderResultWindow(
            report.Summary,
            report.Notes,
            title,
            showCopyButton: true,
            refreshReportAsync: refreshReportAsync);
        // A modeless issue report must be an independent top-level window.
        // Showing it with AIM as its owner keeps it permanently above AIM on
        // Windows, which defeats the purpose of keeping the main mod list usable.
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        return window;
    }

    private void ApplyReport(ReportContent report)
    {
        _report = BuildReport(report.Summary, report.Notes);
        SummaryText.Text = report.Summary;
        SummaryText.IsVisible = !string.IsNullOrWhiteSpace(report.Summary);
        NotesPanel.Children.Clear();
        foreach (var note in report.Notes)
            NotesPanel.Children.Add(CreateNoteControl(note));
    }

    private async Task RefreshReportAsync()
    {
        if (_refreshReportAsync is null) return;

        RefreshButton.IsEnabled = false;
        try
        {
            ApplyReport(await _refreshReportAsync());
        }
        catch (Exception exception)
        {
            SummaryText.Text = exception.Message;
            SummaryText.IsVisible = true;
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private static Control CreateNoteControl(LoadOrderNote note)
    {
        if (note.Kind == LoadOrderNoteKind.FileConflict && note.Details.Count > 0)
        {
            var paths = new StackPanel { Spacing = 4 };
            foreach (var path in note.Details)
            {
                paths.Children.Add(new SelectableTextBlock
                {
                    Text = $"• {path}",
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("monospace")
                });
            }

            return new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Header = new SelectableTextBlock
                {
                    Text = note.Message,
                    TextWrapping = TextWrapping.Wrap
                },
                Content = new ScrollViewer
                {
                    MaxHeight = 220,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = paths
                },
                IsExpanded = false
            };
        }

        // Compatibility, hook, and shortcut notes can contain many lines. Give
        // each its own collapsed card instead of turning the report into one
        // unscannable block of text.
        var (header, detail) = SplitMessage(note.Message);
        if (!string.IsNullOrWhiteSpace(detail))
        {
            return new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Header = new SelectableTextBlock
                {
                    Text = header,
                    TextWrapping = TextWrapping.Wrap
                },
                Content = new ScrollViewer
                {
                    MaxHeight = 220,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new SelectableTextBlock
                    {
                        Text = detail,
                        TextWrapping = TextWrapping.Wrap
                    }
                },
                IsExpanded = false
            };
        }

        return new SelectableTextBlock
        {
            Text = $"• {note.Message}",
            TextWrapping = TextWrapping.Wrap
        };
    }

    private static (string Header, string Detail) SplitMessage(string message)
    {
        var newLine = message.IndexOfAny(['\r', '\n']);
        if (newLine < 0) return (message, string.Empty);

        var detailStart = newLine;
        while (detailStart < message.Length && (message[detailStart] == '\r' || message[detailStart] == '\n'))
            detailStart++;

        return (message[..newLine], message[detailStart..]);
    }

    private static string BuildReport(string summary, IReadOnlyList<LoadOrderNote> notes)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary)) lines.Add(summary);

        foreach (var note in notes)
        {
            lines.Add(note.Message);
            lines.AddRange(note.Details.Select(path => $"  • {path}"));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
}
