using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.GmlMods;

namespace Garethp.ModsOfMistriaGUI.Views;

public partial class LoadOrderResultWindow : Window
{
    public sealed record ReportContent(string Summary, IReadOnlyList<LoadOrderNote> Notes);

    private readonly Func<Task<ReportContent>>? _refreshReportAsync;

    // Not readonly: a refresh replaces the report the window is showing rather than opening a
    // second one, so the summary and the notes are whatever the last scan produced.
    private string _summary;
    private List<LoadOrderNote> _notes;

    /// <summary>Null for the load-order window, which reports actions rather than open questions.</summary>
    private readonly DismissedIssueStore? _dismissedIssues;

    /// <summary>Null when the window is only describing an issue, not offering to fix it.</summary>
    private readonly ConflictReportActions? _actions;

    /// <summary>
    /// Told whenever an issue is ticked off or put back, so the mod list can re-run its sweep.
    ///
    /// The report is modeless: it sits beside the list rather than over it, and it used to tell the
    /// list nothing until it was closed. So a user who worked down a screenful of issues watched the
    /// warning triangles stay exactly where they were, on mods whose only issue they had just
    /// settled - the icon and the report disagreeing for as long as the window stayed open.
    /// </summary>
    private readonly Action? _dismissalsChanged;

    private bool _showDismissed;

    // Rebuilt alongside the visible list so that what "Copy report" produces matches what the
    // window shows, dismissals included.
    private string _report = string.Empty;

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
        Func<Task<ReportContent>>? refreshReportAsync = null,
        DismissedIssueStore? dismissedIssues = null,
        ConflictReportActions? actions = null,
        Action? dismissalsChanged = null)
    {
        InitializeComponent();

        // A dialog taller than the screen's working area is centred with its top edge off
        // the display, which puts the title bar out of reach. See DialogBounds.
        Opened += (_, _) => this.FitToScreen();

        ScrollEnds.Attach(NotesScroller);

        Title = title ?? LocalizationService.Instance["GUILoadOrderTitle"];
        _refreshReportAsync = refreshReportAsync;
        _summary = summary;
        _notes = notes.ToList();
        _dismissedIssues = dismissedIssues;
        _actions = actions;
        _dismissalsChanged = dismissalsChanged;
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

        ShowDismissedToggle.IsCheckedChanged += (_, _) =>
        {
            var wanted = ShowDismissedToggle.IsChecked == true;
            if (wanted == _showDismissed) return;
            _showDismissed = wanted;
            Rebuild();
        };

        Rebuild();
    }

    public static Task ShowAsync(
        Window owner,
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        string? title = null,
        bool showCopyButton = false,
        bool compact = false,
        DismissedIssueStore? dismissedIssues = null,
        ConflictReportActions? actions = null)
    {
        return new LoadOrderResultWindow(
                summary, notes, title, showCopyButton, compact,
                dismissedIssues: dismissedIssues, actions: actions)
            .ShowDialog(owner);
    }

    /// <summary>
    /// Opens a modeless issue report. The caller supplies a deliberate refresh
    /// action because scanning a large mod set should never run for every edit.
    /// </summary>
    public static LoadOrderResultWindow Show(
        ReportContent report,
        string title,
        Func<Task<ReportContent>> refreshReportAsync,
        DismissedIssueStore? dismissedIssues = null,
        ConflictReportActions? actions = null,
        Action? dismissalsChanged = null)
    {
        var window = new LoadOrderResultWindow(
            report.Summary,
            report.Notes,
            title,
            showCopyButton: true,
            refreshReportAsync: refreshReportAsync,
            dismissedIssues: dismissedIssues,
            actions: actions,
            dismissalsChanged: dismissalsChanged);
        // A modeless issue report must be an independent top-level window.
        // Showing it with AIM as its owner keeps it permanently above AIM on
        // Windows, which defeats the purpose of keeping the main mod list usable.
        window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        return window;
    }

    private void ApplyReport(ReportContent report)
    {
        _summary = report.Summary;
        _notes = report.Notes.ToList();
        Rebuild();
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

    private static LocalizedTexts Texts => LocalizedTexts.Instance;

    /// <summary>
    /// Says that the answer to an issue has changed, so the mod list can catch up.
    ///
    /// Guarded because the listener re-scans the mod list: a failure there is not a reason to lose
    /// the tick the user just made, and this is called from event handlers that nothing catches.
    /// </summary>
    private void DismissalsChanged()
    {
        try
        {
            _dismissalsChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Logger.Log($"Refreshing the mod list after an issue was settled failed: {exception}");
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redraws the whole list.
    ///
    /// Acting on an issue moves it between two sections, changes its wording, or removes a mod from
    /// it, so the cheapest correct thing is to rebuild rather than to patch controls in place. A
    /// conflict report is a few dozen rows at worst.
    /// </summary>
    private void Rebuild()
    {
        NotesPanel.Children.Clear();
        _report = BuildReport(_summary, _notes, _dismissedIssues);
        SummaryText.Text = _summary;
        SummaryText.IsVisible = !string.IsNullOrWhiteSpace(_summary);

        if (_dismissedIssues is null)
        {
            foreach (var note in _notes)
                NotesPanel.Children.Add(CreateNoteControl(note));
            ShowDismissedToggle.IsVisible = false;
            return;
        }

        var live = new List<LoadOrderNote>();
        var dismissed = new List<LoadOrderNote>();
        foreach (var note in _notes)
            (_dismissedIssues.IsDismissed(note.StableKey) ? dismissed : live).Add(note);

        foreach (var note in live)
            NotesPanel.Children.Add(CreateDismissableRow(note, isDismissed: false));

        ShowDismissedToggle.IsVisible = dismissed.Count > 0;
        ShowDismissedToggle.Content = string.Format(Texts.GUIIssueShowDismissed, dismissed.Count);

        if (dismissed.Count == 0 || !_showDismissed) return;

        NotesPanel.Children.Add(new TextBlock
        {
            Text = Texts.GUIIssueDismissedHeader,
            FontWeight = FontWeight.SemiBold,
            Opacity = 0.7,
            Margin = new Avalonia.Thickness(0, 12, 0, 0)
        });

        foreach (var note in dismissed)
            NotesPanel.Children.Add(CreateDismissableRow(note, isDismissed: true));
    }

    /// <summary>
    /// One issue with the checkbox that says "I have looked at this and it is fine".
    ///
    /// A dismissed issue is dimmed and struck through rather than deleted: the user needs to be
    /// able to find it again and change their mind, and a silently vanishing warning is exactly the
    /// behaviour that makes people distrust the report.
    /// </summary>
    private Control CreateDismissableRow(LoadOrderNote note, bool isDismissed)
    {
        var checkbox = new CheckBox
        {
            IsChecked = isDismissed,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Avalonia.Thickness(0, 2, 8, 0),
            MinWidth = 0
        };
        ToolTip.SetTip(checkbox, Texts.GUIIssueDismissTooltip);

        checkbox.IsCheckedChanged += (_, _) =>
        {
            var wanted = checkbox.IsChecked == true;
            if (wanted == isDismissed) return;

            _dismissedIssues?.SetDismissed(note.StableKey, wanted, note.Message);

            // Immediately, not when the window closes: the row in the mod list behind this one is
            // showing a warning triangle for the issue that was just settled.
            DismissalsChanged();

            // The dismissed section is left exactly as the user set it. Ticking a box used to force
            // it open, which on a list with fifty already-judged issues meant one click unfolded a
            // screenful of things the user had deliberately put away - and the row they had just
            // dealt with was somewhere in the middle of it. The count on the toggle going up by one
            // is trace enough of where the issue went.

            // Rebuilding tears down the very checkbox whose event is still being dispatched, so
            // let this event finish first.
            Dispatcher.UIThread.Post(Rebuild);
        };

        var content = CreateNoteControl(note, isDismissed);
        Grid.SetColumn(content, 1);

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Opacity = isDismissed ? 0.55 : 1.0
        };
        row.Children.Add(checkbox);
        row.Children.Add(content);
        return row;
    }

    private Control CreateNoteControl(LoadOrderNote note, bool struckThrough = false)
    {
        var decorations = struckThrough ? TextDecorations.Strikethrough : null;
        var (header, detail) = SplitMessage(note.Message);

        var body = BuildBody(note, detail);
        if (body is null)
        {
            return new SelectableTextBlock
            {
                Text = $"• {note.Message}",
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = decorations
            };
        }

        return new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Header = new SelectableTextBlock
            {
                Text = header,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = decorations
            },
            // Wrapped rather than used directly: an expanded conflict can be seventy-four file
            // paths deep, and the buttons that act on it are above them. Without a way back this
            // is the one place in AIM where reading the detail costs you the controls.
            Content = ScrollEnds.Wrap(new ScrollViewer
            {
                MaxHeight = 320,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = body
            }),
            IsExpanded = false
        };
    }

    /// <summary>
    /// What sits inside an issue's expander: the rest of its message, the mods involved with the
    /// buttons that act on each, the files at stake, and the way out to the research window.
    /// Returns null when there is nothing worth expanding.
    /// </summary>
    private Control? BuildBody(LoadOrderNote note, string detail)
    {
        var panel = new StackPanel { Spacing = 8 };

        if (!string.IsNullOrWhiteSpace(detail))
        {
            panel.Children.Add(new SelectableTextBlock
            {
                Text = detail,
                TextWrapping = TextWrapping.Wrap
            });
        }

        foreach (var participant in note.Participants)
            panel.Children.Add(CreateParticipantRow(note, participant));

        var research = CreateResearchRow(note);

        if (note.Details.Count > 0)
        {
            var paths = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 4, 0, 0) };
            paths.Children.Add(new TextBlock
            {
                Text = Texts.GUIConflictSharedFilesHeader,
                FontWeight = FontWeight.SemiBold,
                Opacity = 0.75
            });

            foreach (var path in note.Details)
            {
                paths.Children.Add(new SelectableTextBlock
                {
                    Text = $"• {path}",
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("monospace")
                });
            }

            // "Find a fix" belongs beside the file list, not under it. A cosmetics conflict lists
            // seventy-odd sprite paths, and the one control that does something about them sat at
            // the bottom of all of it - so the user had to scroll past every file to reach the
            // button, having already decided from the first three lines what the conflict was. The
            // paths take the width they need and the button sits in the space to their right, which
            // on a wide report is empty anyway.
            if (research is not null)
            {
                Grid.SetColumn(research, 1);
                research.VerticalAlignment = VerticalAlignment.Top;
                research.HorizontalAlignment = HorizontalAlignment.Right;

                var withAction = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                    HorizontalAlignment = HorizontalAlignment.Stretch
                };
                withAction.Children.Add(paths);
                withAction.Children.Add(research);

                panel.Children.Add(withAction);
                return panel;
            }

            panel.Children.Add(paths);
        }

        // No file list to sit beside - a hook or hotkey clash - so it keeps its old place.
        if (research is not null) panel.Children.Add(research);

        return panel.Children.Count == 0 ? null : panel;
    }

    // ── One mod inside an issue ──────────────────────────────────────────────────

    /// <summary>
    /// A mod's name, with everything else about it a hover away.
    ///
    /// The path used to be printed inline. With three mods installed under long Steam paths that
    /// turned a one-line shortcut warning into six wrapped lines of directory names, and the only
    /// thing the user needed - which mods - was the hardest part to find.
    /// </summary>
    private Control CreateParticipantRow(LoadOrderNote note, IssueParticipant participant)
    {
        var isCurrentWinner = note.Kind == LoadOrderNoteKind.FileConflict &&
                              ReferenceEquals(participant, note.Participants[^1]);

        var name = new TextBlock
        {
            Text = participant.Display,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var tip = participant.Detail.Length > 0
            ? $"{participant.SourcePath}\n{participant.Detail}"
            : participant.SourcePath;
        ToolTip.SetTip(name, tip);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { name }
        };

        if (isCurrentWinner)
        {
            row.Children.Add(new Border
            {
                Background = Brushes.Gray,
                CornerRadius = new Avalonia.CornerRadius(3),
                Padding = new Avalonia.Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = Texts.GUIConflictCurrentWinner, Foreground = Brushes.White }
            });
        }

        var action = note.Kind switch
        {
            LoadOrderNoteKind.FileConflict when !isCurrentWinner => CreateWinnerButton(note, participant),
            LoadOrderNoteKind.HotkeyConflict => CreateRebindButton(note, participant),
            _ => null
        };
        if (action is not null) row.Children.Add(action);

        return row;
    }

    private Control? CreateWinnerButton(LoadOrderNote note, IssueParticipant participant)
    {
        if (_actions is null) return null;

        var button = new Button { Content = Texts.GUIConflictMakeThisWin };
        ToolTip.SetTip(button, Texts.GUIConflictMakeThisWinTooltip);

        button.Click += (_, _) =>
        {
            if (!_actions.MakeModWin(note, participant)) return;

            // The note's own wording named the old winner, so it has to be rewritten rather than
            // left to contradict the list underneath it. The issue key is built from mod ids and
            // versions, neither of which reordering changes, so any dismissal survives this.
            var reordered = note.Participants.Where(other => !ReferenceEquals(other, participant))
                .Append(participant)
                .ToList();

            Replace(note, note with
            {
                Message = string.Format(Texts.GUIConflictWinnerNow, participant.Display),
                Participants = reordered
            });
        };

        return button;
    }

    private Control? CreateRebindButton(LoadOrderNote note, IssueParticipant participant)
    {
        if (_actions is null) return null;

        var capability = _actions.InspectRebind(note, participant);
        var button = new Button
        {
            Content = Texts.GUIHotkeyRebindButton,
            IsEnabled = capability.CanRebind
        };
        ToolTip.SetTip(button, capability.CanRebind
            ? string.Format(Texts.GUIHotkeyRebindTooltip, string.Join(", ", capability.Bindings))
            : DescribeBlocker(capability.Blocker));

        button.Click += async (_, _) =>
        {
            var newKey = await _actions.RebindHotkey(note, participant);
            if (newKey is null) return;

            var remaining = note.Participants.Where(other => !ReferenceEquals(other, participant)).ToList();

            // One mod cannot clash with itself, so moving the second-to-last mod off the key ends
            // the issue. Mark it resolved rather than leaving a warning about a conflict the user
            // has just fixed - that was the whole point of the button.
            if (remaining.Count < 2)
            {
                var solved = note with
                {
                    Message = string.Format(Texts.GUIHotkeyReboundResolved, participant.Display, newKey),
                    Participants = remaining
                };
                _dismissedIssues?.SetDismissed(
                    note.StableKey, true, solved.Message,
                    new IssueVerdict(DismissedIssueStore.VerdictRebound, null, $"{participant.Display} → {newKey}"));

                // As with the checkbox: the dismissed section stays however the user left it, and
                // the mod list hears about it now rather than at closing time.
                DismissalsChanged();
                Replace(note, solved);
                return;
            }

            Replace(note, note with
            {
                Message = string.Format(Texts.GUIHotkeyRebound, participant.Display, newKey, note.HotkeyKey ?? ""),
                Participants = remaining
            });
        };

        return button;
    }

    private static string DescribeBlocker(RebindBlocker blocker) => blocker switch
    {
        RebindBlocker.NotAFolder => Texts.GUIHotkeyBlockedArchive,
        RebindBlocker.NotADeclaredBinding => Texts.GUIHotkeyBlockedNotDeclared,
        RebindBlocker.NoFreeKeys => Texts.GUIHotkeyBlockedNoFreeKeys,
        RebindBlocker.NotWritable => Texts.GUIHotkeyBlockedUnreadable,
        _ => ""
    };

    // ── Research ─────────────────────────────────────────────────────────────────

    private Control? CreateResearchRow(LoadOrderNote note)
    {
        // Only issues about two or more mods have a "do they actually get along" question to
        // answer. A single mod's compatibility warning is about the mod itself.
        if (_actions is null || note.Participants.Count < 2) return null;
        if (note.Kind is not (LoadOrderNoteKind.FileConflict or LoadOrderNoteKind.HookConflict
            or LoadOrderNoteKind.HotkeyConflict)) return null;

        var button = new Button
        {
            Content = Texts.GUIConflictFindAFix,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 6, 0, 0)
        };
        ToolTip.SetTip(button, Texts.GUIConflictFindAFixTooltip);

        button.Click += async (_, _) =>
        {
            var verdict = await _actions.Research(this, note);
            if (verdict is null) return;

            var resolved = verdict.Kind is DismissedIssueStore.VerdictNotAnIssue
                or DismissedIssueStore.VerdictPatchExists;

            if (resolved)
            {
                // As with the checkbox: the dismissed section stays however the user left it.
                _dismissedIssues?.SetDismissed(note.StableKey, true, note.Message, verdict);
            }
            else
            {
                // "They really are incompatible" is an answer, not a resolution: the user still has
                // to disable one of them, so the issue stays visible with the finding attached.
                _dismissedIssues?.SetVerdict(note.StableKey, verdict);
            }

            DismissalsChanged();
            Rebuild();
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { button } };

        var recorded = _dismissedIssues?.Verdict(note.StableKey);
        if (recorded is not null)
        {
            var label = new TextBlock
            {
                Text = DescribeVerdict(recorded),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(0, 6, 0, 0)
            };
            row.Children.Add(label);

            if (ExternalUrl.IsAllowed(recorded.Link))
            {
                var open = new Button
                {
                    Content = Texts.GUIResearchOpenPatch,
                    Margin = new Avalonia.Thickness(0, 6, 0, 0)
                };
                ToolTip.SetTip(open, recorded.Link);
                open.Click += (_, _) => ExternalUrl.Open(recorded.Link);
                row.Children.Add(open);
            }
        }

        return row;
    }

    private static string DescribeVerdict(IssueVerdict verdict) => verdict.Kind switch
    {
        DismissedIssueStore.VerdictNotAnIssue => Texts.GUIVerdictNotAnIssue,
        DismissedIssueStore.VerdictPatchExists => Texts.GUIVerdictPatchExists,
        DismissedIssueStore.VerdictIncompatible => Texts.GUIVerdictIncompatible,
        DismissedIssueStore.VerdictRebound => string.Format(Texts.GUIVerdictRebound, verdict.Note ?? ""),
        _ => ""
    };

    // ── Plumbing ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Swaps one note for an updated copy of itself.
    ///
    /// Matched by identity rather than by <c>IndexOf</c>: <see cref="LoadOrderNote"/> is a record,
    /// so equality is structural, and two notes of the same kind carrying the same message would be
    /// indistinguishable to it - which would silently rewrite the wrong row.
    /// </summary>
    private void Replace(LoadOrderNote original, LoadOrderNote updated)
    {
        var index = _notes.FindIndex(note => ReferenceEquals(note, original));
        if (index < 0) return;

        _notes[index] = updated;
        Dispatcher.UIThread.Post(Rebuild);
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

    /// <summary>
    /// The plain-text version behind "Copy report".
    ///
    /// Dismissed findings stay in it - a report pasted into a bug thread should not quietly omit
    /// things - but they are labelled, so the reader can see which ones the user had already
    /// judged and which are still open.
    /// </summary>
    private static string BuildReport(
        string summary,
        IReadOnlyList<LoadOrderNote> notes,
        DismissedIssueStore? dismissedIssues)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary)) lines.Add(summary);

        var marker = LocalizationService.Instance["GUIIssueDismissedMarker"];

        foreach (var note in notes)
        {
            var isDismissed = dismissedIssues?.IsDismissed(note.StableKey) == true;
            lines.Add(isDismissed ? $"{marker} {note.Message}" : note.Message);
            lines.AddRange(note.Participants.Select(participant =>
                $"  - {participant.Display} [{participant.SourcePath}]"));
            lines.AddRange(note.Details.Select(path => $"  • {path}"));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, lines);
    }
}
