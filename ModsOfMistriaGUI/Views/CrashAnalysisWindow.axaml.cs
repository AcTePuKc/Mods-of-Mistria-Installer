using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Crash;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// "Check Crashes": what broke the game, which mod did it, and what to do next.
///
/// The issue report answers "what might break". This answers "what did break", which is a different
/// question with a different kind of evidence behind it - the game's own crash log, rather than
/// AIM's reading of the mods - and it is answered in the same three passes, cheapest first:
///
///   1. It reads the backtrace against the archive AIM built. Every mod's code is installed under a
///      directory named after that mod, so a crash inside one names its mod outright; a crash in
///      engine code that was reading mod data names the data set, and AIM knows who writes to it.
///      This costs nothing, needs no network, and is usually the whole answer.
///   2. It reads the suspects' Nexus pages - bug tracker first, then comments, description and
///      changelog - because a crash somebody has already reported is a crash with a known answer,
///      and often a fixed one.
///   3. It offers to do something, and then to check that the something worked: switch the mod off,
///      rebuild the game, launch it, and watch. Every edit it makes to a mod is snapshotted first,
///      and every snapshot is offered back on the spot.
///
/// The one thing it will not do is submit anything on the user's behalf. The bug report is written
/// for them, put on their clipboard and the tracker opened; posting it is theirs.
/// </summary>
public partial class CrashAnalysisWindow : Window
{
    private readonly CrashContext? _context;
    private readonly NexusApiClient? _client;
    private readonly CrashArchive _archive = new();

    private IReadOnlyList<GameCrashLog> _crashes = [];
    private GameCrashLog? _crash;
    private CrashDiagnosis? _diagnosis;
    private bool _busy;

    /// <summary>
    /// Fixes already worked out, by mod id.
    ///
    /// Working them out means reading every data file in a mod, and the suspect cards are rebuilt
    /// after every verification run - so without this a user working through a seven-mod shortlist
    /// pays for the same scan of the same seven mods seven times over. An entry is dropped the
    /// moment a fix is applied to that mod, because the file it was about has changed.
    /// </summary>
    private readonly Dictionary<string, IReadOnlyList<ModRepair>> _repairs =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shared and short-timeout, for the pages Nexus has no API for.</summary>
    private static readonly HttpClient PageReader = new() { Timeout = TimeSpan.FromSeconds(20) };

    /// <summary>
    /// How long a verification run is watched before AIM stops waiting.
    ///
    /// Long enough to get past the loading screen and into a save, which is where boot crashes and
    /// most data crashes happen, and short enough that a user who has started actually playing is
    /// told "no crash" rather than left staring at a spinner. Reaching it is a pass, not a timeout.
    /// </summary>
    private static readonly TimeSpan VerifyWindow = TimeSpan.FromMinutes(4);

    // Required by Avalonia's compiled XAML loader; normal callers use ShowAsync.
    public CrashAnalysisWindow() : this(null, null)
    {
    }

    private CrashAnalysisWindow(CrashContext? context, NexusApiClient? client)
    {
        InitializeComponent();

        _context = context;
        _client = client;

        var texts = LocalizedTexts.Instance;

        Title = texts.GUICrashTitle;
        PickerLabel.Text = texts.GUICrashPickerLabel;
        OpenFolderButton.Content = texts.GUICrashOpenFolder;
        DiagnosisHeader.Text = texts.GUICrashDiagnosisHeader;
        SuspectsHeader.Text = texts.GUICrashSuspectsHeader;
        SuspectsHint.Text = texts.GUICrashSuspectsHint;
        VerifyHeader.Text = texts.GUICrashVerifyHeader;
        VerifyHint.Text = texts.GUICrashVerifyHint;
        VerifyButton.Content = texts.GUICrashVerifyButton;
        VerifyResetButton.Content = texts.GUICrashRestartHunt;
        LinksHeader.Text = texts.GUICrashLinksHeader;
        RawExpander.Header = texts.GUICrashRawHeader;
        BacktraceExpander.Header = texts.GUICrashBacktraceHeader;
        CopyButton.Content = texts.GUICrashCopy;
        RefreshButton.Content = texts.GUICrashRefresh;
        CloseButton.Content = texts.GUIClose;

        ToolTip.SetTip(CopyButton, texts.GUICrashCopyTooltip);
        ToolTip.SetTip(OpenFolderButton, texts.GUICrashOpenFolderTooltip);
        ToolTip.SetTip(VerifyButton, texts.GUICrashVerifyTooltip);
        ToolTip.SetTip(VerifyNextButton, texts.GUICrashNextCandidateTooltip);
        ToolTip.SetTip(VerifyResetButton, texts.GUICrashRestartHuntTooltip);

        // Above the button, always: these sit on the bottom edge of the window, where a tooltip
        // placed below is flipped back over the control it describes - and then the click meant
        // for the button only dismisses the tooltip. See ConflictResearchWindow.
        foreach (var button in new Control[]
                     { CopyButton, RefreshButton, CloseButton, VerifyButton, VerifyNextButton, VerifyResetButton })
            ToolTip.SetPlacement(button, Avalonia.Controls.PlacementMode.Top);

        CloseButton.Click += (_, _) => Close();
        CopyButton.Click += async (_, _) => await CopyAsync();
        RefreshButton.Click += async (_, _) => await LoadAsync();
        OpenFolderButton.Click += (_, _) => ExternalUrl.OpenFolder(_archive.Folder);
        VerifyButton.Click += async (_, _) => await VerifyAsync();
        VerifyNextButton.Click += async (_, _) => await VerifyNextAsync();
        VerifyResetButton.Click += (_, _) => RestartHunt();

        CrashPicker.SelectionChanged += async (_, _) => await SelectAsync();

        ScrollEnds.Attach(BodyScroller);

        Opened += async (_, _) =>
        {
            this.FitToScreen();

            // Nothing may escape this handler: it is an async void by the event's signature, and
            // everything below reads files somebody else wrote. Losing a section of this window is
            // a bad outcome; taking AIM down over a malformed crash log is a worse one.
            try
            {
                await LoadAsync();
            }
            catch (Exception exception)
            {
                Logger.Log($"The crash window could not open: {exception}");
                StatusText.Text = string.Format(LocalizedTexts.Instance.GUICrashFailed, exception.Message);
            }
        };
    }

    public static async Task ShowAsync(Window owner, CrashContext? context, NexusApiClient? client)
    {
        var window = new CrashAnalysisWindow(context, client);
        await window.ShowDialog(owner);
    }

    // ── Loading ──────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        var texts = LocalizedTexts.Instance;

        StatusText.Text = texts.GUICrashLooking;

        // Off the UI thread: this reads a folder of files and, on the way, the game's own crash log
        // out of a directory that may be on a slow disk.
        _crashes = await Task.Run(() => _archive.All());

        if (_crashes.Count == 0)
        {
            Clear();
            CrashPicker.ItemsSource = Array.Empty<string>();
            CrashPicker.IsEnabled = false;
            StatusText.Text = texts.GUICrashNone;
            return;
        }

        CrashPicker.IsEnabled = true;
        CrashPicker.ItemsSource = _crashes.Select(Describe).ToList();
        CrashPicker.SelectedIndex = 0;

        // Selecting fires SelectionChanged, which does the analysis - but only if the index really
        // changed, and on a refresh it may already be 0.
        await SelectAsync();
    }

    private static string Describe(GameCrashLog crash)
    {
        var mods = crash.ModsAtLaunch is { Count: > 0 }
            ? $" — {crash.ModsAtLaunch.Count} mods"
            : "";

        return $"{crash.When.LocalDateTime:g}{mods} — {Shorten(crash.Tidied)}";
    }

    private static string Shorten(string text) =>
        text.Length <= 80 ? text : text[..77].TrimEnd() + "…";

    private async Task SelectAsync()
    {
        var index = CrashPicker.SelectedIndex;
        if (index < 0 || index >= _crashes.Count) return;

        var chosen = _crashes[index];
        if (ReferenceEquals(chosen, _crash)) return;

        _crash = chosen;

        try
        {
            await AnalyseAsync(chosen);
        }
        catch (Exception exception)
        {
            Logger.Log($"Analysing a crash failed: {exception}");
            StatusText.Text = string.Format(LocalizedTexts.Instance.GUICrashFailed, exception.Message);
        }
    }

    private void Clear()
    {
        DiagnosisSection.IsVisible = false;
        SuspectsSection.IsVisible = false;
        SettledSection.IsVisible = false;
        VerifySection.IsVisible = false;
        StaleBanner.IsVisible = false;
        ForSection.IsVisible = false;
        AgainstSection.IsVisible = false;
        CautionSection.IsVisible = false;
        FindingsSection.IsVisible = false;
        LinksSection.IsVisible = false;
        VerifyStatus.IsVisible = false;
        VerifySteps.IsVisible = false;
        VerifyProgressText.IsVisible = false;
        VerifyNextButton.IsVisible = false;
        VerifyResetButton.IsVisible = false;

        VerifySteps.Children.Clear();
        DiagnosisReasons.Children.Clear();
        BacktracePanel.Children.Clear();
        SuspectsPanel.Children.Clear();
        SettledPanel.Children.Clear();
        ForPanel.Children.Clear();
        AgainstPanel.Children.Clear();
        CautionPanel.Children.Clear();
        FindingsPanel.Children.Clear();
        LinksPanel.Children.Clear();
        RawText.Text = "";
    }

    // ── The answer AIM can give on its own ───────────────────────────────────────

    private async Task AnalyseAsync(GameCrashLog crash)
    {
        var texts = LocalizedTexts.Instance;

        Clear();
        RawText.Text = crash.RawReport;
        StatusText.Text = texts.GUICrashReading;

        if (_context is null)
        {
            // Without the mod list there is still a crash worth showing - the message, the
            // backtrace, the copy button - just nobody to blame for it.
            ShowBacktrace(crash, []);
            StatusText.Text = texts.GUICrashNoModList;
            return;
        }

        var context = _context;

        var diagnosis = await Task.Run(() =>
        {
            using var source = CrashSourceIndex.Open(context.MistriaLocation);
            return CrashDiagnoser.Diagnose(crash, context.Enabled, source, context.InstalledAt);
        });

        _diagnosis = diagnosis;

        StaleBanner.IsVisible = diagnosis.Stale;
        if (diagnosis.Stale) StaleText.Text = texts.GUICrashStale;

        DiagnosisSection.IsVisible = true;
        DiagnosisHeadline.Text = diagnosis.Headline;

        DiagnosisSection.BorderBrush = new SolidColorBrush(Color.Parse(
            diagnosis.Stale ? "#d08a2a" : diagnosis.AnyCertain ? "#e05252" : "#4c8bf5"));

        foreach (var reason in diagnosis.Reasons)
            DiagnosisReasons.Children.Add(new SelectableTextBlock
            {
                Text = "• " + reason,
                TextWrapping = TextWrapping.Wrap
            });

        ShowBacktrace(crash, diagnosis.Sources);
        ShowSuspects(diagnosis);
        ShowVerify(diagnosis);

        StatusText.Text = _client is null ? texts.GUICrashNoApiKey : texts.GUICrashWorking;

        await ResearchAsync(diagnosis);
    }

    private void ShowBacktrace(GameCrashLog crash, IReadOnlyList<CrashSource> sources)
    {
        BacktraceExpander.Header = string.Format(
            LocalizedTexts.Instance.GUICrashBacktraceCount, crash.Frames.Count);

        BacktracePanel.Children.Add(new SelectableTextBlock
        {
            Text = crash.Tidied,
            TextWrapping = TextWrapping.Wrap,
            FontWeight = FontWeight.SemiBold
        });

        foreach (var frame in crash.Frames)
        {
            var lines = new StackPanel { Spacing = 2 };

            lines.Children.Add(new SelectableTextBlock
            {
                Text = $"{frame.Index}: {frame.Path}:{frame.Line}",
                TextWrapping = TextWrapping.Wrap,
                FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
                FontSize = 12
            });

            var source = sources.FirstOrDefault(entry =>
                entry.Path == frame.Path && entry.Line == frame.Line);

            if (source is not null)
            {
                if (source.Function is not null)
                    lines.Children.Add(new TextBlock
                    {
                        Text = $"in {source.Function}()",
                        Opacity = 0.75,
                        FontSize = 12
                    });

                lines.Children.Add(new SelectableTextBlock
                {
                    Text = string.Join("\n", source.Context),
                    TextWrapping = TextWrapping.NoWrap,
                    FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
                    FontSize = 11,
                    Opacity = 0.85
                });
            }

            BacktracePanel.Children.Add(new Border
            {
                BorderThickness = new Avalonia.Thickness(2, 0, 0, 0),

                // Frame 0 is where it actually broke; the rest are how it got there, and colouring
                // them the same makes the user read all of them to find the one that matters.
                BorderBrush = frame.Index == 0 ? Brushes.OrangeRed : Brushes.Gray,
                Padding = new Avalonia.Thickness(10, 2, 0, 2),
                Child = lines
            });
        }
    }

    // ── The mods it points at ────────────────────────────────────────────────────

    /// <summary>
    /// The shortlist, in two parts: the mods still under suspicion, and the ones a run has already
    /// answered.
    ///
    /// They are separated rather than merely tinted because the question the user is holding is
    /// "what is left to try", and a single list answers it only by being read end to end and
    /// counted. The settled ones are kept - the evidence is still worth reading, and a verdict can
    /// be retested - but they are folded away and said as a number, so the live list is the live
    /// list.
    /// </summary>
    private void ShowSuspects(CrashDiagnosis diagnosis)
    {
        var texts = LocalizedTexts.Instance;

        var caught = new List<CrashSuspect>();
        var open = new List<CrashSuspect>();
        var cleared = new List<CrashSuspect>();

        foreach (var suspect in diagnosis.Suspects)
            switch (VerdictFor(suspect))
            {
                // A mod a run has caught goes to the top of the live list, not into the fold-away.
                // It is the answer the user came for, and everything they might do next - the
                // fixes, the mod's own bug thread, removing it - hangs off its card.
                case CrashTrialVerdict.Guilty: caught.Add(suspect); break;
                case CrashTrialVerdict.Cleared: cleared.Add(suspect); break;
                default: open.Add(suspect); break;
            }

        SuspectsSection.IsVisible = caught.Count > 0 || open.Count > 0;

        foreach (var suspect in caught.Concat(open))
            SuspectsPanel.Children.Add(CreateSuspect(suspect, diagnosis));

        SettledSection.IsVisible = cleared.Count > 0;

        if (cleared.Count == 0) return;

        SettledHeader.Text = string.Format(
            texts.GUICrashSettledHeader, cleared.Count, diagnosis.Suspects.Count);

        SettledExpander.Header = texts.GUICrashSettledExpander;

        foreach (var suspect in cleared)
            SettledPanel.Children.Add(CreateSuspect(suspect, diagnosis));
    }

    private static (Color Accent, string Badge) Style(CrashConfidence confidence)
    {
        var texts = LocalizedTexts.Instance;

        return confidence switch
        {
            CrashConfidence.Certain => (Color.Parse("#e05252"), texts.GUICrashBadgeCertain),
            CrashConfidence.Strong => (Color.Parse("#d05a2a"), texts.GUICrashBadgeStrong),
            CrashConfidence.Likely => (Color.Parse("#d08a2a"), texts.GUICrashBadgeLikely),
            _ => (Color.Parse("#8a8a8a"), texts.GUICrashBadgePossible)
        };
    }

    private static Border Chip(Color colour, string text) => new()
    {
        Background = new SolidColorBrush(colour),
        CornerRadius = new Avalonia.CornerRadius(3),
        Padding = new Avalonia.Thickness(6, 1),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeight.Bold,
            Foreground = Brushes.White
        }
    };

    private Control CreateSuspect(CrashSuspect suspect, CrashDiagnosis diagnosis)
    {
        var texts = LocalizedTexts.Instance;
        var (accent, badgeText) = Style(suspect.Confidence);

        var badge = Chip(accent, badgeText);

        // What a run has already proved about this mod, said on the mod's own card. The confidence
        // badge is AIM's reading of the evidence; this is the answer an actual run gave, and it
        // outranks the reading - which is why a ruled-out mod is greyed rather than left looking
        // like a live accusation.
        var verdict = VerdictFor(suspect);

        // A mark the user made is shown as a mark the user made. It carries the same weight in the
        // hunt - it takes the mod out of the queue either way - but it is not something AIM found
        // out, and a month later the difference between "AIM proved this" and "I decided this" is
        // the whole of what the user needs to know to judge their own shortlist.
        var manual = IsManual(suspect);

        var verdictChip = verdict switch
        {
            CrashTrialVerdict.Cleared when manual =>
                Chip(Color.Parse("#4c8bf5"), texts.GUICrashTagMarkedInnocent),
            CrashTrialVerdict.Guilty when manual =>
                Chip(Color.Parse("#8a3fb5"), texts.GUICrashTagMarkedCulprit),
            CrashTrialVerdict.Cleared => Chip(Color.Parse("#3fa34d"), texts.GUICrashTagRuledOut),
            CrashTrialVerdict.Guilty => Chip(Color.Parse("#e05252"), texts.GUICrashTagLikelyCause),
            CrashTrialVerdict.Inconclusive => Chip(Color.Parse("#8a8a8a"), texts.GUICrashTagTested),
            _ => null
        };

        var heading = new WrapPanel();
        heading.Children.Add(badge);

        if (verdictChip is not null)
        {
            verdictChip.Margin = new Avalonia.Thickness(6, 0, 0, 0);
            heading.Children.Add(verdictChip);
        }

        heading.Children.Add(new SelectableTextBlock
        {
            Text = suspect.Name,
            FontWeight = FontWeight.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Avalonia.Thickness(8, 0, 0, 0)
        });

        var body = new StackPanel { Spacing = 6, Children = { heading } };

        // What being caught means, said on the card rather than only in the verify panel below -
        // which is where the user is not looking once they have their answer, and which is cleared
        // the next time the window reloads.
        if (verdict == CrashTrialVerdict.Guilty)
            body.Children.Add(new SelectableTextBlock
            {
                Text = manual ? texts.GUICrashMarkedCulpritNote : texts.GUICrashCulpritNote,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                FontWeight = FontWeight.SemiBold
            });

        foreach (var evidence in suspect.Evidence)
            body.Children.Add(new SelectableTextBlock
            {
                Text = "• " + evidence,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
                FontSize = 13
            });

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };

        var actions = new WrapPanel();

        var page = PageFor(suspect);

        if (page is not null)
        {
            var open = new Button { Content = texts.GUICrashOpenModPage, Margin = new Avalonia.Thickness(0, 0, 8, 6) };
            open.Click += (_, _) => ExternalUrl.Open(page);
            ToolTip.SetTip(open, page);
            actions.Children.Add(open);
        }

        // The whole point of "wait for an update": the report is written, so reporting it costs a
        // paste rather than an evening of working out what the author needs to know.
        var report = new Button { Content = texts.GUICrashReportBug, Margin = new Avalonia.Thickness(0, 0, 8, 6) };
        report.Click += async (_, _) => await ReportAsync(suspect, diagnosis, status);
        ToolTip.SetTip(report, texts.GUICrashReportBugTooltip);
        actions.Children.Add(report);

        if (_context?.Disable is not null && _context.RunAndWatch is not null)
        {
            var disable = new Button { Content = texts.GUICrashDisableAndCheck, Margin = new Avalonia.Thickness(0, 0, 8, 6) };
            disable.Click += async (_, _) => await DisableAndVerifyAsync(suspect, status);
            ToolTip.SetTip(disable, texts.GUICrashDisableAndCheckTooltip);
            actions.Children.Add(disable);
        }

        // The manual way back for a mod that is ruled out and still switched off. AIM re-enables a
        // cleared mod itself, so this button is normally absent; it appears when that did not
        // happen - the row was gone at the time, the list refused it - and it is exactly then that
        // the user needs it, because the alternative is remembering which of eight mods AIM
        // exonerated and ticking them by hand.
        var enable = _context?.Enable;

        if (enable is not null &&
            verdict == CrashTrialVerdict.Cleared &&
            !IsOnNow(suspect.ModId))
        {
            var back = new Button { Content = texts.GUICrashSwitchBackOn, Margin = new Avalonia.Thickness(0, 0, 8, 6) };
            ToolTip.SetTip(back, texts.GUICrashSwitchBackOnTooltip);

            back.Click += (_, _) =>
            {
                try
                {
                    if (enable(suspect.ModId))
                    {
                        // Said in the verify panel rather than on the card, because the refresh
                        // that follows replaces the card - and with it this status line.
                        Say(true, string.Format(texts.GUICrashSwitchedBackOn, suspect.Name));
                        RefreshSuspects();
                    }
                    else
                    {
                        Report(status, false, string.Format(texts.GUICrashCannotSwitchBackOn, suspect.Name));
                    }
                }
                catch (Exception exception)
                {
                    Logger.Log($"Switching {suspect.Name} back on from the crash window failed: {exception}");
                    Report(status, false, exception.Message);
                }
            };

            actions.Children.Add(back);
        }

        // Saying so yourself.
        //
        // A four-minute supervised run is the strongest evidence AIM can gather and the slowest, and
        // it is not always the evidence available. A user may have seen this exact crash on a
        // machine that never had the mod, or may know the mod is the culprit because the author has
        // said so in the bug thread the window is showing them. Making them sit through a run to
        // tell AIM something they already know is the kind of thing that gets a shortlist abandoned
        // half-finished - and an abandoned hunt is one where AIM keeps re-accusing everything.
        //
        // The marks are the same verdicts a run records, so they take a mod out of the queue, out of
        // the picker and out of the live list exactly as a run would. They are labelled as the
        // user's own throughout, and one click takes any of them back.
        if (_context?.Trials is not null)
        {
            if (verdict != CrashTrialVerdict.Cleared)
                actions.Children.Add(MarkButton(
                    suspect, status,
                    texts.GUICrashMarkInnocent, texts.GUICrashMarkInnocentTooltip,
                    CrashTrialVerdict.Cleared));

            if (verdict != CrashTrialVerdict.Guilty)
                actions.Children.Add(MarkButton(
                    suspect, status,
                    texts.GUICrashMarkCulprit, texts.GUICrashMarkCulpritTooltip,
                    CrashTrialVerdict.Guilty));

            // Only offered for the user's own marks. A verdict a run reached is undone by "Start
            // over", which throws away the whole hunt, because a single run's result is not the
            // sort of thing it makes sense to quietly delete one of.
            if (manual)
                actions.Children.Add(MarkButton(
                    suspect, status,
                    texts.GUICrashUnmark, texts.GUICrashUnmarkTooltip,
                    CrashTrialVerdict.Untested));
        }

        // Held in a local so the lambda does not have to reason about a field that could, as far as
        // the compiler knows, have changed by the time it runs. Same reasoning as the research
        // window's remove button.
        var removeMod = _context?.RemoveMod;

        if (removeMod is not null)
        {
            var remove = new Button { Content = texts.GUICrashRemoveMod, Margin = new Avalonia.Thickness(0, 0, 8, 6) };

            remove.Click += async (_, _) =>
            {
                // An async void by the event's signature: nothing may escape it.
                try
                {
                    if (await removeMod(suspect.ModId))
                        Report(status, true, string.Format(texts.GUICrashRemoved, suspect.Name));
                }
                catch (Exception exception)
                {
                    Logger.Log($"Removing {suspect.Name} from the crash window failed: {exception}");
                    Report(status, false, exception.Message);
                }
            };

            actions.Children.Add(remove);
        }

        body.Children.Add(actions);

        // AIM's own fixes come before the type-it-in-yourself form, because when there is one it is
        // the better answer: it names the line, shows the change, and needs nothing from the user
        // but a decision.
        var repairs = RepairPanel(suspect, status);
        if (repairs is not null) body.Children.Add(repairs);

        var edit = EditPanel(suspect, status);
        if (edit is not null) body.Children.Add(edit);

        var versions = VersionPanel(suspect, status);
        if (versions is not null) body.Children.Add(versions);

        body.Children.Add(status);

        // A mod a run has cleared keeps its card - the evidence that put it there is still worth
        // reading, and the user may want to retest it - but it stops shouting. The colour follows
        // the verdict rather than the original suspicion, because the verdict is the newer and
        // better-founded of the two.
        var edge = verdict switch
        {
            CrashTrialVerdict.Cleared => Color.Parse("#3fa34d"),
            CrashTrialVerdict.Guilty => Color.Parse("#e05252"),
            _ => accent
        };

        return new Border
        {
            Background = new SolidColorBrush(edge, 0.06),
            BorderThickness = new Avalonia.Thickness(3, 1, 1, 1),
            BorderBrush = new SolidColorBrush(edge),
            CornerRadius = new Avalonia.CornerRadius(0, 6, 6, 0),
            Padding = new Avalonia.Thickness(12, 8, 12, 8),
            Opacity = verdict == CrashTrialVerdict.Cleared ? 0.6 : 1,
            Child = body
        };
    }

    private string? PageFor(CrashSuspect suspect)
    {
        var provenance = _context?.Provenance(suspect.ModId);
        var candidate = provenance?.PageUrl ?? _context?.Find(suspect.ModId)?.GetDownloadUrl();

        return candidate is not null && ExternalUrl.IsAllowed(candidate) ? candidate : null;
    }

    // ── Fixes AIM worked out for itself ──────────────────────────────────────────

    /// <summary>
    /// The repairs AIM is prepared to make without being told what to write.
    ///
    /// This is the form of the feature that matters: not "AIM can edit a mod", which it already
    /// could, but "AIM found the broken line and can show you it". The panel is built empty and
    /// filled in behind the user, because working out the fixes means reading every data file in
    /// the mod and there may be a dozen mods on screen.
    ///
    /// Nothing here applies itself. Every fix is shown as the two lines it changes, with the reason
    /// it is justified, and waits for a decision - which is the only honest way to offer an edit to
    /// somebody else's work, however sure the tool is.
    /// </summary>
    private Control? RepairPanel(CrashSuspect suspect, TextBlock status)
    {
        if (_context?.Repairs is null || _context.ApplyRepair is null) return null;

        var texts = LocalizedTexts.Instance;
        var list = new StackPanel { Spacing = 6 };

        var panel = new StackPanel
        {
            Spacing = 6,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),

            // Hidden until there is something to show. A heading over an empty box on every suspect
            // would be a promise the panel usually cannot keep.
            IsVisible = false,
            Children =
            {
                new TextBlock { Text = texts.GUICrashRepairHeader, FontWeight = FontWeight.SemiBold },
                new TextBlock
                {
                    Text = texts.GUICrashRepairHint,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75,
                    FontSize = 12
                },
                list
            }
        };

        _ = FillRepairsAsync(suspect, panel, list, status);

        return panel;
    }

    private async Task FillRepairsAsync(
        CrashSuspect suspect, Control panel, StackPanel list, TextBlock status)
    {
        var find = _context?.Repairs;
        if (find is null) return;

        if (!_repairs.TryGetValue(suspect.ModId, out var found))
        {
            try
            {
                // Off the UI thread: this reads every .toml in the mod.
                found = await Task.Run(() => find(suspect.ModId));
            }
            catch (Exception exception)
            {
                // A scan that fails offers nothing, which is a fine outcome for an optional panel.
                Logger.Log($"Could not work out fixes for {suspect.Name}: {exception}");
                return;
            }

            _repairs[suspect.ModId] = found;
        }

        if (found.Count == 0) return;

        foreach (var repair in found) list.Children.Add(RepairCard(repair, suspect, list, status));

        panel.IsVisible = true;
    }

    private Control RepairCard(
        ModRepair repair, CrashSuspect suspect, StackPanel list, TextBlock status)
    {
        var texts = LocalizedTexts.Instance;

        var apply = new Button { Content = texts.GUICrashRepairApply };
        var card = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                new SelectableTextBlock
                {
                    Text = repair.Title,
                    FontWeight = FontWeight.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                },
                new SelectableTextBlock
                {
                    Text = repair.Why,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85,
                    FontSize = 12
                },
                new SelectableTextBlock
                {
                    Text = $"{repair.Path}:{repair.Line}\n{repair.Diff}",
                    FontFamily = FontFamily.Parse("Consolas, Menlo, monospace"),
                    FontSize = 11,

                    // The diff is the thing being approved, so it must not be reflowed into
                    // something that no longer lines up with what will be written to disk.
                    TextWrapping = TextWrapping.NoWrap
                },
                apply
            }
        };

        apply.Click += async (_, _) =>
        {
            // An async void by the event's signature: nothing may escape it.
            try
            {
                var applyRepair = _context?.ApplyRepair;
                if (applyRepair is null) return;

                var confirm = await MessageBoxManager.GetMessageBoxStandard(
                    texts.GUICrashRepairHeader,
                    string.Format(texts.GUICrashRepairConfirm,
                        repair.Why, $"{repair.Path}:{repair.Line}", repair.Diff),
                    ButtonEnum.YesNo).ShowAsync();

                if (confirm != ButtonResult.Yes) return;

                apply.IsEnabled = false;

                var outcome = await applyRepair(suspect.ModId, repair);

                if (!outcome.Applied)
                {
                    apply.IsEnabled = true;
                    Report(status, false, outcome.Message);
                    return;
                }

                // Gone from the list: the line it was about no longer says what it said, so
                // offering it again would be offering an edit that cannot apply. The rest of the
                // fixes for this mod stay, and their line numbers are unchanged because a repair
                // rewrites a line rather than adding or removing one.
                list.Children.Remove(card);

                // The mod on disk is no longer what the cached scan described.
                _repairs.Remove(suspect.ModId);

                Report(status, true, string.Format(texts.GUICrashRepairApplied, suspect.Name));
            }
            catch (Exception exception)
            {
                Logger.Log($"Applying AIM's fix to {suspect.Name} failed: {exception}");
                apply.IsEnabled = true;
                Report(status, false, exception.Message);
            }
        };

        return new Border
        {
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Brushes.Gray,
            CornerRadius = new Avalonia.CornerRadius(4),
            Padding = new Avalonia.Thickness(10, 6),
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                Content = card
            }
        };
    }

    // ── Applying a fix somebody else worked out ──────────────────────────────────

    /// <summary>
    /// The form for "the bug thread says to change line 14".
    ///
    /// AIM does not read the fix out of the thread. It cannot: a post saying "just add an icon to
    /// the category" is a sentence, not an edit, and a tool that guesses at what somebody meant and
    /// then writes it into their mod folder would be worse than no tool. So the change is the
    /// user's, typed in having read the post - and everything around it is AIM's: the snapshot
    /// taken first, the marker on the mod's row, and the entry in the dropdown just below that puts
    /// it back.
    ///
    /// The other button is the blunter version of the same idea. When a whole file is the problem -
    /// a data file whose entries the game cannot load - setting it aside disables that file and
    /// leaves the rest of the mod installed, which is often what "wait for an update" should
    /// actually mean in the meantime.
    /// </summary>
    private Control? EditPanel(CrashSuspect suspect, TextBlock status)
    {
        var replaceLine = _context?.ReplaceLine;
        var setAside = _context?.SetAside;

        if (replaceLine is null && setAside is null) return null;

        var texts = LocalizedTexts.Instance;

        // The diagnosis often already knows the file and line - a frame inside the mod's own code,
        // or an entry traced back to one of its data files - so the form starts where the evidence
        // points rather than empty.
        var (path, line) = Split(suspect.Where);

        var file = new TextBox { Text = path, Watermark = texts.GUICrashEditFile, MinWidth = 260 };
        var number = new TextBox { Text = line, Watermark = texts.GUICrashEditLine, Width = 70 };
        var replacement = new TextBox
        {
            Watermark = texts.GUICrashEditText,
            AcceptsReturn = false,
            FontFamily = FontFamily.Parse("Consolas, Menlo, monospace")
        };

        var buttons = new WrapPanel();

        if (replaceLine is not null)
        {
            var apply = new Button { Content = texts.GUICrashEditApply, Margin = new Avalonia.Thickness(0, 0, 8, 0) };

            apply.Click += async (_, _) =>
            {
                // An async void by the event's signature: nothing may escape it.
                try
                {
                    var target = file.Text?.Trim() ?? "";

                    if (target.Length == 0 || !int.TryParse(number.Text?.Trim(), out var at) || at < 1)
                    {
                        Report(status, false, texts.GUICrashEditNeedFile);
                        return;
                    }

                    var summary = string.Format(texts.GUICrashEditSummary, target, at, suspect.Name);

                    var confirm = await MessageBoxManager.GetMessageBoxStandard(
                        texts.GUICrashEditHeader,
                        string.Format(texts.GUICrashEditConfirm, summary),
                        ButtonEnum.YesNo).ShowAsync();

                    if (confirm != ButtonResult.Yes) return;

                    var outcome = await replaceLine(
                        suspect.ModId, target, at, replacement.Text ?? "", summary);

                    Report(status, outcome.Applied, outcome.Message);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Editing {suspect.Name} from the crash window failed: {exception}");
                    Report(status, false, exception.Message);
                }
            };

            buttons.Children.Add(apply);
        }

        if (setAside is not null)
        {
            var aside = new Button { Content = texts.GUICrashEditSetAside };
            ToolTip.SetTip(aside, texts.GUICrashEditSetAsideTooltip);

            aside.Click += async (_, _) =>
            {
                try
                {
                    var target = file.Text?.Trim() ?? "";

                    if (target.Length == 0)
                    {
                        Report(status, false, texts.GUICrashEditNeedFile);
                        return;
                    }

                    var summary = string.Format(texts.GUICrashEditAsideSummary, target, suspect.Name);

                    var confirm = await MessageBoxManager.GetMessageBoxStandard(
                        texts.GUICrashEditHeader,
                        string.Format(texts.GUICrashEditConfirm, summary),
                        ButtonEnum.YesNo).ShowAsync();

                    if (confirm != ButtonResult.Yes) return;

                    var outcome = await setAside(suspect.ModId, [target], summary);
                    Report(status, outcome.Applied, outcome.Message);
                }
                catch (Exception exception)
                {
                    Logger.Log($"Setting a file aside in {suspect.Name} failed: {exception}");
                    Report(status, false, exception.Message);
                }
            };

            buttons.Children.Add(aside);
        }

        var inside = new StackPanel
        {
            Spacing = 6,
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
            Children =
            {
                new TextBlock { Text = texts.GUICrashEditHint, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children = { file, number }
                },
                replacement,
                buttons
            }
        };

        return new Expander
        {
            Header = texts.GUICrashEditHeader,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = inside
        };
    }

    /// <summary>
    /// Splits the diagnosis's "where" into a file and a line, when it is shaped like one.
    ///
    /// It is a display string rather than a structured field, and its two shapes are
    /// "gml/scripts/thing.gml:42" for a code frame and "stores line 263" for an entry in the built
    /// data. Only the first names a file inside the mod, so only the first is offered as a
    /// starting point; the second would prefill a path that does not exist in the mod folder, which
    /// is worse than prefilling nothing.
    /// </summary>
    private static (string Path, string Line) Split(string? where)
    {
        if (string.IsNullOrWhiteSpace(where)) return ("", "");

        var colon = where.LastIndexOf(':');
        if (colon <= 0 || colon == where.Length - 1) return ("", "");

        var tail = where[(colon + 1)..].Trim();

        return int.TryParse(tail, out _) ? (where[..colon].Trim(), tail) : ("", "");
    }

    // ── Undoing what AIM changed ─────────────────────────────────────────────────

    /// <summary>
    /// Every copy of this mod AIM can put back, offered here rather than only on the row.
    ///
    /// A user who has just applied a fix from a bug thread and watched the game crash anyway is in
    /// this window, not looking at the mod list, and "put it back the way it was" is the next thing
    /// they want. The snapshot taken immediately before AIM's edit is named as such, because "the
    /// version from before you changed it" is what they mean and a timestamp is not.
    /// </summary>
    private Control? VersionPanel(CrashSuspect suspect, TextBlock status)
    {
        if (_context?.Versions is null) return null;

        IReadOnlyList<VersionChoice> versions;
        var history = _context.EditHistory?.Invoke(suspect.ModId) ?? [];

        try { versions = _context.Versions(suspect.ModId); }
        catch (Exception exception)
        {
            Logger.Log($"Could not list versions for {suspect.Name}: {exception.Message}");
            return null;
        }

        if (versions.Count == 0 && history.Count == 0) return null;

        var texts = LocalizedTexts.Instance;
        var inside = new StackPanel { Spacing = 6, Margin = new Avalonia.Thickness(0, 6, 0, 0) };

        if (history.Count > 0)
        {
            inside.Children.Add(new TextBlock
            {
                Text = texts.GUICrashEditedByAim,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.8
            });

            foreach (var entry in history)
                inside.Children.Add(new SelectableTextBlock
                {
                    Text = "• " + entry,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12,
                    Opacity = 0.75
                });

            var putBack = _context.PutBack;

            if (putBack is not null)
            {
                var undo = new Button
                {
                    Content = texts.GUICrashUndoEdits,
                    HorizontalAlignment = HorizontalAlignment.Left
                };

                ToolTip.SetTip(undo, texts.GUICrashUndoEditsTooltip);

                undo.Click += async (_, _) =>
                {
                    try
                    {
                        var outcome = await putBack(suspect.ModId);
                        Report(status, outcome.Applied, outcome.Message);
                    }
                    catch (Exception exception)
                    {
                        Logger.Log($"Undoing AIM's edits to {suspect.Name} failed: {exception}");
                        Report(status, false, exception.Message);
                    }
                };

                inside.Children.Add(undo);
            }
        }

        var restoreVersion = _context.RestoreVersion;

        if (versions.Count > 0 && restoreVersion is not null)
        {
            var picker = new ComboBox
            {
                ItemsSource = versions.Select(version => version.Label).ToList(),
                SelectedIndex = 0,
                MinWidth = 260
            };

            var restore = new Button { Content = texts.GUICrashRestoreVersion };

            restore.Click += async (_, _) =>
            {
                var chosen = picker.SelectedIndex;
                if (chosen < 0 || chosen >= versions.Count) return;

                try
                {
                    if (await restoreVersion(suspect.ModId, versions[chosen]))
                        Report(status, true, string.Format(
                            texts.GUICrashRestored, suspect.Name, versions[chosen].Label));
                }
                catch (Exception exception)
                {
                    Logger.Log($"Restoring {suspect.Name} failed: {exception}");
                    Report(status, false, exception.Message);
                }
            };

            inside.Children.Add(new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children = { picker, restore }
            });
        }

        return new Expander
        {
            Header = texts.GUICrashVersionsHeader,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = inside
        };
    }

    // ── Reporting it ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the bug report, puts it on the clipboard, and opens the tracker.
    ///
    /// Deliberately stops there. AIM has the user's Nexus session and could post this; it must not.
    /// A report filed in somebody's name that they have not read is not a report, and an author who
    /// starts receiving machine-written bug threads stops reading the tracker.
    /// </summary>
    private async Task ReportAsync(CrashSuspect suspect, CrashDiagnosis diagnosis, TextBlock status)
    {
        if (_crash is null) return;

        var texts = LocalizedTexts.Instance;

        try
        {
            var provenance = _context?.Provenance(suspect.ModId);

            // The mods that share the same corner of the game. It is the first thing every author
            // asks and the thing users least often think to include.
            var others = diagnosis.Suspects
                .Where(other => !string.Equals(other.ModId, suspect.ModId, StringComparison.OrdinalIgnoreCase))
                .Select(other => other.Name)
                .ToList();

            var composed = CrashReportComposer.ForSuspect(
                suspect, _crash, diagnosis, _context?.Find(suspect.ModId), others,
                provenance?.NexusModId, provenance?.PageUrl);

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null)
                await clipboard.SetTextAsync($"{composed.Title}\n\n{composed.Body}");

            if (composed.Url is not null && ExternalUrl.IsAllowed(composed.Url))
            {
                ExternalUrl.Open(composed.Url);
                Report(status, true, texts.GUICrashReportReady);
            }
            else
                Report(status, true, texts.GUICrashReportCopiedOnly);
        }
        catch (Exception exception)
        {
            Logger.Log($"Composing a bug report failed: {exception}");
            Report(status, false, exception.Message);
        }
    }

    private async Task CopyAsync()
    {
        if (_crash is null) return;

        try
        {
            var text = _diagnosis is null
                ? _crash.RawReport
                : CrashReportComposer.ForClipboard(_crash, _diagnosis);

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(text);

            StatusText.Text = LocalizedTexts.Instance.GUICrashCopied;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not copy the crash report: {exception.Message}");
        }
    }

    // ── Checking whether it is fixed ─────────────────────────────────────────────

    /// <summary>The crash a verdict is recorded against. Empty when there is no crash loaded.</summary>
    private string TrialKey => _crash?.StableKey ?? "";

    /// <summary>The version of a suspect as it is on disk, so a verdict cannot outlive an update.</summary>
    private string? VersionOf(string modId) => _context?.Find(modId)?.GetVersion();

    private CrashTrialVerdict VerdictFor(CrashSuspect suspect) =>
        _context?.Trials?.VerdictFor(TrialKey, suspect.ModId, VersionOf(suspect.ModId))
        ?? CrashTrialVerdict.Untested;

    /// <summary>The whole verdict, for the places that need to know who reached it.</summary>
    private CrashTrial? TrialFor(CrashSuspect suspect) =>
        _context?.Trials?.Trial(TrialKey, suspect.ModId, VersionOf(suspect.ModId));

    /// <summary>True when the standing verdict is the user's own judgement rather than a run's.</summary>
    private bool IsManual(CrashSuspect suspect) => TrialFor(suspect)?.Manual == true;

    private void ShowVerify(CrashDiagnosis diagnosis)
    {
        var context = _context;
        if (context?.RunAndWatch is null || context.Reinstall is null) return;

        VerifySection.IsVisible = true;

        // A fresh diagnosis starts on "change nothing". Carrying the previous crash's selection
        // over would point the button at whichever mod happened to sit at that index in a list
        // that is now a different list.
        RefreshVerifyPicker(reset: true);
        RefreshHuntProgress();
    }

    /// <summary>
    /// Rebuilds the picker so each suspect carries what is already known about it.
    ///
    /// Offering "Mod A, Mod B, Mod C" unchanged after Mod A has been ruled out invites the user to
    /// spend another four-minute run learning what they learned twenty minutes ago.
    /// </summary>
    private void RefreshVerifyPicker(bool reset = false)
    {
        var suspects = _diagnosis?.Suspects ?? [];
        var selected = reset ? 0 : VerifyPicker.SelectedIndex;

        VerifyPicker.ItemsSource = suspects
            .Select(suspect => (Verdict: VerdictFor(suspect), Manual: IsManual(suspect)) switch
            {
                (CrashTrialVerdict.Cleared, true) =>
                    $"{suspect.Name} — {LocalizedTexts.Instance.GUICrashTagMarkedInnocent}",
                (CrashTrialVerdict.Guilty, true) =>
                    $"{suspect.Name} — {LocalizedTexts.Instance.GUICrashTagMarkedCulprit}",
                (CrashTrialVerdict.Cleared, _) =>
                    $"{suspect.Name} — {LocalizedTexts.Instance.GUICrashTagRuledOut}",
                (CrashTrialVerdict.Guilty, _) =>
                    $"{suspect.Name} — {LocalizedTexts.Instance.GUICrashTagLikelyCause}",
                (CrashTrialVerdict.Inconclusive, _) =>
                    $"{suspect.Name} — {LocalizedTexts.Instance.GUICrashTagTested}",
                _ => suspect.Name
            })
            .Prepend(LocalizedTexts.Instance.GUICrashVerifyNothing)
            .ToList();

        VerifyPicker.SelectedIndex = selected >= 0 && selected <= suspects.Count ? selected : 0;
    }

    /// <summary>
    /// The line under the buttons: how far through the shortlist the user is, and what is next.
    ///
    /// This is the part that turns a sequence of one-off checks into a search with an end. Without
    /// it a user who has ruled out four of seven mods has no way to tell that from a user who has
    /// ruled out none, and both of them give up in the same place.
    /// </summary>
    private void RefreshHuntProgress()
    {
        var texts = LocalizedTexts.Instance;
        var suspects = _diagnosis?.Suspects ?? [];
        var trials = _context?.Trials;

        if (suspects.Count == 0)
        {
            VerifyProgressText.IsVisible = false;
            VerifyNextButton.IsVisible = false;
            VerifyResetButton.IsVisible = false;
            return;
        }

        var tested = suspects.Count(suspect =>
            VerdictFor(suspect) is CrashTrialVerdict.Cleared or CrashTrialVerdict.Guilty);

        var lines = new List<string>();

        if (trials is null) lines.Add(texts.GUICrashTrialsNotKept);
        else if (tested > 0) lines.Add(string.Format(texts.GUICrashTrialProgress, tested, suspects.Count));

        var next = NextCandidate();

        if (next is not null)
        {
            VerifyNextButton.Content = string.Format(texts.GUICrashNextCandidate, next.Name);
            VerifyNextButton.IsVisible = !_busy;
        }
        else
        {
            VerifyNextButton.IsVisible = false;

            // Only worth saying once the user has actually been through them. "Nothing left to try"
            // as an opening line would be nonsense.
            if (tested > 0 && tested >= suspects.Count) lines.Add(texts.GUICrashNoMoreCandidates);
        }

        VerifyResetButton.IsVisible = tested > 0 && !_busy;

        VerifyProgressText.Text = string.Join("  ", lines);
        VerifyProgressText.IsVisible = lines.Count > 0;
    }

    /// <summary>
    /// The next suspect worth a run: strongest first, skipping the ones already answered.
    ///
    /// A mod that is already switched off is skipped too - switching it off again would test
    /// nothing, and the run that switched it off has either already been recorded or was the user's
    /// own doing, in which case AIM has no business claiming credit for the result.
    /// </summary>
    private CrashSuspect? NextCandidate() =>
        (_diagnosis?.Suspects ?? []).FirstOrDefault(suspect =>
            VerdictFor(suspect) is CrashTrialVerdict.Untested or CrashTrialVerdict.Inconclusive &&
            IsOnNow(suspect.ModId));

    /// <summary>
    /// Whether a suspect is switched on at this moment, asked of the mod list when it will answer
    /// and falling back to the snapshot the window opened with when it will not.
    ///
    /// The snapshot is the set the game had when it crashed, and trials move mods in and out of the
    /// list underneath it. Reading it to decide what to test next would keep offering a mod that
    /// has just been switched off, and skip one that has just come back.
    /// </summary>
    private bool IsOnNow(string modId) =>
        _context?.IsEnabled is { } live
            ? live(modId)
            : _context?.Enabled.Any(mod =>
                string.Equals(mod.GetId(), modId, StringComparison.OrdinalIgnoreCase)) == true;

    private async Task VerifyNextAsync()
    {
        var next = NextCandidate();
        if (next is null) return;

        var suspects = _diagnosis?.Suspects ?? [];
        var index = IndexOf(suspects, next.ModId);
        if (index < 0) return;

        VerifyPicker.SelectedIndex = index + 1;
        await VerifyAsync();
    }

    /// <summary>Throws away every verdict for this crash and offers the whole shortlist again.</summary>
    private void RestartHunt()
    {
        if (_busy) return;

        _context?.Trials?.ForgetCrash(TrialKey);

        Say(null, LocalizedTexts.Instance.GUICrashRestartedHunt);
        RefreshVerifyPicker();
        RefreshSuspects();
        RefreshHuntProgress();
    }

    private static int IndexOf(IReadOnlyList<CrashSuspect> suspects, string modId)
    {
        for (var i = 0; i < suspects.Count; i++)
            if (string.Equals(suspects[i].ModId, modId, StringComparison.OrdinalIgnoreCase))
                return i;

        return -1;
    }

    /// <summary>Rebuilds the suspect cards so a verdict just recorded shows on the card it is about.</summary>
    private void RefreshSuspects()
    {
        var diagnosis = _diagnosis;
        if (diagnosis is null) return;

        SuspectsPanel.Children.Clear();
        SettledPanel.Children.Clear();
        ShowSuspects(diagnosis);
    }

    private async Task DisableAndVerifyAsync(CrashSuspect suspect, TextBlock status)
    {
        var index = IndexOf(_diagnosis?.Suspects ?? [], suspect.ModId);
        if (index < 0) return;

        // Drive the panel below rather than duplicating the sequence: one place decides what
        // "disable, rebuild, run" means, and the user watches it happen where they can read it.
        // The picker's first entry is "change nothing", so the suspects are offset by one.
        VerifyPicker.SelectedIndex = index + 1;

        Report(status, true, LocalizedTexts.Instance.GUICrashHandedToVerify);
        VerifySection.BringIntoView();

        await VerifyAsync();
    }

    /// <summary>
    /// Switch the chosen mod off, rebuild the game archive, start the game, watch - and then say
    /// what that proved.
    ///
    /// The first four steps, because any three of them is a check that lies. Disabling a mod
    /// changes nothing the game can see until the archive is rebuilt - the mods are compiled into
    /// it - so a "disable and try again" that skipped the rebuild would run the identical game and
    /// report that disabling made no difference.
    ///
    /// The fifth step is the one that makes this a search rather than a coin toss. A run where the
    /// crash comes back has not failed: it has proved the mod that was switched off is innocent,
    /// which is exactly as useful as proving one guilty and is the more common outcome by far. So
    /// that verdict is written down, the mod goes back on - leaving it off would slowly strip the
    /// user's game for no reason - and the next suspect is offered by name. Without this the user
    /// does the work of elimination and AIM throws the answers away between runs.
    /// </summary>
    private async Task VerifyAsync()
    {
        var context = _context;
        if (_busy || context?.RunAndWatch is null || context.Reinstall is null) return;

        var texts = LocalizedTexts.Instance;
        var chosen = VerifyPicker.SelectedIndex - 1;
        var suspect = chosen >= 0 && _diagnosis is not null && chosen < _diagnosis.Suspects.Count
            ? _diagnosis.Suspects[chosen]
            : null;

        if (suspect is not null && context.Disable is null)
        {
            Say(false, texts.GUICrashCannotDisable);
            return;
        }

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            texts.GUICrashVerifyHeader,
            suspect is null
                ? texts.GUICrashVerifyConfirmNothing
                : string.Format(texts.GUICrashVerifyConfirm, suspect.Name),
            ButtonEnum.YesNo).ShowAsync();

        if (confirm != ButtonResult.Yes) return;

        // Read before the mod list is touched: a verdict is about the code that was on disk when
        // the run happened, and this is that moment.
        var crashKey = TrialKey;
        var version = suspect is null ? null : VersionOf(suspect.ModId);

        _busy = true;
        VerifyButton.IsEnabled = false;
        VerifyNextButton.IsVisible = false;
        VerifyResetButton.IsVisible = false;

        VerifySteps.Children.Clear();
        VerifySteps.IsVisible = true;

        try
        {
            if (suspect is not null)
            {
                if (context.Disable is null || !context.Disable(suspect.ModId))
                {
                    Step(string.Format(texts.GUICrashDisableFailed, suspect.Name), false);
                    Say(false, string.Format(texts.GUICrashDisableFailed, suspect.Name));
                    return;
                }

                Step(string.Format(texts.GUICrashDisabled, suspect.Name));
            }
            else
            {
                Step(texts.GUICrashStepNothingChanged);
            }

            Step(texts.GUICrashRebuilding);

            var failure = await context.Reinstall();

            if (failure is not null)
            {
                Step(string.Format(texts.GUICrashRebuildFailed, failure), false);
                Say(false, string.Format(texts.GUICrashRebuildFailed, failure));

                // A rebuild that did not happen tested nothing, so the suspect keeps its place in
                // the queue rather than being quietly counted as answered.
                RecordTrial(crashKey, suspect, version, CrashTrialVerdict.Inconclusive, failure);
                return;
            }

            Step(texts.GUICrashStepRebuilt);
            Step(texts.GUICrashStepStarting);

            // Said before the run rather than after it. This is the step the user waits through -
            // four minutes with the game in front of them - and a line that only appears once the
            // game has closed is a line that explains the wait to somebody who has finished waiting.
            Step(string.Format(texts.GUICrashStepWatching, (int)VerifyWindow.TotalMinutes));

            var outcome = await context.RunAndWatch(VerifyWindow);

            if (!outcome.Started)
            {
                Step(texts.GUICrashStepNotStarted, false);
                Say(false, outcome.Message);
                RecordTrial(crashKey, suspect, version, CrashTrialVerdict.Inconclusive, outcome.Message);
                return;
            }

            Step(outcome.Message, !outcome.Crashed);

            await JudgeAsync(crashKey, suspect, version, outcome);
        }
        catch (Exception exception)
        {
            Logger.Log($"Verifying a crash fix failed: {exception}");
            Step(exception.Message, false);
            Say(false, exception.Message);
        }
        finally
        {
            _busy = false;
            VerifyButton.IsEnabled = true;
            RefreshVerifyPicker();
            RefreshSuspects();
            RefreshHuntProgress();
        }
    }

    /// <summary>
    /// Turns "the game crashed / did not crash" into "this mod is or is not the cause", and acts on
    /// the answer.
    ///
    /// The distinction that matters is between the same crash and a different one. The same crash
    /// coming back without the suspect clears the suspect outright. A *different* crash also clears
    /// it for the original question - the thing being tested for did not happen - but it is new
    /// evidence about a new fault, so the window loads that instead rather than leaving the user
    /// reading a report about a crash that has been superseded.
    /// </summary>
    private async Task JudgeAsync(
        string crashKey, CrashSuspect? suspect, string? version, GameRunOutcome outcome)
    {
        var texts = LocalizedTexts.Instance;

        // Something ended the game that was not the crash under investigation - it was killed, or
        // it left a log AIM could not read. The suspect keeps its place in the queue: counting this
        // as a clearance would rule a mod out on the strength of a graphics driver falling over.
        if (outcome.Inconclusive)
        {
            RecordTrial(crashKey, suspect, version, CrashTrialVerdict.Inconclusive, outcome.Message);
            Say(false, outcome.Message);
            return;
        }

        if (!outcome.Crashed)
        {
            if (suspect is null)
            {
                Say(true, texts.GUICrashVerdictNothingGuilty);
                return;
            }

            RecordTrial(crashKey, suspect, version, CrashTrialVerdict.Guilty, outcome.Message);

            // Left switched off, deliberately and now durably. This is the mod that was proved to
            // break the game; switching it back on is the user's decision to make with the evidence
            // in front of them, not something AIM should do quietly on their behalf.
            RefreshCrasherMark(suspect);

            Say(true, string.Format(texts.GUICrashVerdictGuilty, suspect.Name));

            // The shortlist becomes a single named culprit, and the answer the user now wants is
            // "what do I do about it" - so the fixes AIM can apply and whatever the mod's own pages
            // say about this crash are fetched for that one mod, rather than left as they were for
            // a shortlist that no longer exists.
            RefreshSuspects();
            RefreshHuntProgress();
            await ResearchCulpritAsync(suspect);
            return;
        }

        var same = outcome.Crash is null ||
                   string.Equals(outcome.Crash.StableKey, crashKey, StringComparison.Ordinal);

        if (suspect is null)
        {
            // Nothing was changed, so nothing is proved about any mod. Show the new crash if it is
            // a new one; otherwise leave the report as it is. The message is said *after* the
            // reload, because reloading clears the panel it is written into.
            if (!same) await LoadAsync();
            Say(false, outcome.Message);
            return;
        }

        RecordTrial(crashKey, suspect, version, CrashTrialVerdict.Cleared, outcome.Message);

        // Back on. A mod ruled out is a mod the user should still have: leaving each cleared
        // suspect switched off would, over a seven-mod hunt, quietly uninstall six things that were
        // never the problem - and every later run would then be testing a game that is missing them.
        var restored = _context?.Enable?.Invoke(suspect.ModId) == true;

        if (!same)
        {
            // The verdict is already written, so reloading onto the new crash keeps it. The hunt for
            // the old crash is still there if the user picks it in the crash list again. Reload
            // first: it clears the panel the verdict is written into.
            await LoadAsync();
            Say(false, string.Format(texts.GUICrashVerdictDifferentCrash, suspect.Name));
            return;
        }

        Say(false, string.Format(
            restored ? texts.GUICrashVerdictCleared : texts.GUICrashVerdictClearedNoReenable,
            suspect.Name));
    }

    private Button MarkButton(
        CrashSuspect suspect, TextBlock status, string label, string tooltip, CrashTrialVerdict verdict)
    {
        var button = new Button { Content = label, Margin = new Avalonia.Thickness(0, 0, 8, 6) };
        ToolTip.SetTip(button, tooltip);

        button.Click += async (_, _) =>
        {
            // An async void by the event's signature: nothing may escape it.
            try
            {
                await MarkByHandAsync(suspect, status, verdict);
            }
            catch (Exception exception)
            {
                Logger.Log($"Marking {suspect.Name} by hand failed: {exception}");
                Report(status, false, exception.Message);
            }
        };

        return button;
    }

    /// <summary>
    /// Records the user's own verdict on a mod, and does the same things to the mod list that the
    /// equivalent run-proved verdict would.
    ///
    /// The consequences are deliberately identical, because the point of the mark is to let the user
    /// tell AIM something true that AIM cannot test. Halving the response - recording the opinion
    /// but leaving the mod switched the way it was - would leave the user marking a culprit and then
    /// discovering the game still loads it.
    /// </summary>
    private async Task MarkByHandAsync(CrashSuspect suspect, TextBlock status, CrashTrialVerdict verdict)
    {
        var texts = LocalizedTexts.Instance;
        var trials = _context?.Trials;

        if (trials is null || _busy) return;

        var crashKey = TrialKey;
        var version = VersionOf(suspect.ModId);

        if (string.IsNullOrEmpty(crashKey))
        {
            Report(status, false, texts.GUICrashCannotMarkWithoutCrash);
            return;
        }

        try
        {
            trials.Record(crashKey, suspect.ModId, version, verdict, texts.GUICrashMarkedByHandNote, manual: true);
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not record a manual verdict for {suspect.Name}: {exception}");
            Report(status, false, exception.Message);
            return;
        }

        switch (verdict)
        {
            // Marked as the cause: off it goes, and the row is marked, exactly as when a run catches
            // one. Left ticked, the "culprit" would still be in the next build of the archive.
            case CrashTrialVerdict.Guilty:
                var off = _context?.Disable?.Invoke(suspect.ModId) == true;
                RefreshCrasherMark(suspect);
                Say(true, string.Format(
                    off ? texts.GUICrashMarkedCulprit : texts.GUICrashMarkedCulpritAlreadyOff, suspect.Name));
                break;

            // Marked innocent: the accusation is dropped and, if AIM is the reason it is switched
            // off, it goes back on. A mod the user has vouched for should not stay missing from
            // their game because of a hunt they have just ended.
            case CrashTrialVerdict.Cleared:
                RefreshCrasherMark(suspect);
                var back = !IsOnNow(suspect.ModId) && _context?.Enable?.Invoke(suspect.ModId) == true;
                Say(true, string.Format(
                    back ? texts.GUICrashMarkedInnocentAndOn : texts.GUICrashMarkedInnocent, suspect.Name));
                break;

            // Taking a mark back leaves the mod switched however it is now. AIM does not know what
            // the user has done with it since, and guessing would undo their work rather than its own.
            default:
                RefreshCrasherMark(suspect);
                Say(null, string.Format(texts.GUICrashUnmarked, suspect.Name));
                break;
        }

        RefreshVerifyPicker();
        RefreshSuspects();
        RefreshHuntProgress();

        // Same follow-through as a run-proved culprit gets: once there is a named cause, the
        // question is what to do about it, and the answer is in AIM's own repairs and on the mod's
        // pages. A user who marked it by hand is if anything further along - they already believe
        // they know - so making them go and look this up themselves would be the wrong half to skip.
        if (verdict == CrashTrialVerdict.Guilty) await ResearchCulpritAsync(suspect);
    }

    /// <summary>
    /// Brings the mod's row in AIM into line with the verdict just recorded.
    ///
    /// The verdict is worth nothing where it was earned. The user closes this window and is back at
    /// a list of two hundred checkboxes, one of which is unticked for a reason they will not
    /// remember next month - and the obvious thing to do with a mod that is switched off for no
    /// visible reason is to switch it on.
    /// </summary>
    private void RefreshCrasherMark(CrashSuspect suspect)
    {
        try
        {
            _context?.RefreshCrasherMark?.Invoke(suspect.ModId);
        }
        catch (Exception exception)
        {
            // The verdict is already written and the mod is already switched the right way. Losing
            // the badge costs a reminder; throwing here would cost the user the verdict itself.
            Logger.Log($"Could not update the crash mark on {suspect.Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads the culprit's own pages now that there is a culprit.
    ///
    /// The research at load time deliberately spends its page fetches on the mods AIM thinks are
    /// likely, which is a guess. A proved culprit is not a guess, and it is worth a fetch even if
    /// AIM had it fourth on the list - this is the point at which "has anybody else hit this, and
    /// did the author fix it" stops being idle curiosity and becomes the user's next move.
    /// </summary>
    private async Task ResearchCulpritAsync(CrashSuspect suspect)
    {
        var texts = LocalizedTexts.Instance;

        if (_client is null)
        {
            StatusText.Text = texts.GUICrashNoApiKey;
            return;
        }

        var provenance = _context?.Provenance(suspect.ModId);
        var subject = new ResearchSubject(suspect.Name, provenance?.NexusModId, provenance?.PageUrl);

        StatusText.Text = string.Format(texts.GUICrashResearchingCulprit, suspect.Name);

        ResearchResult result;

        try
        {
            result = await ConflictResearch.InvestigateAsync([subject], _client, PageReader);
        }
        catch (Exception exception)
        {
            Logger.Log($"Research on the culprit {suspect.Name} failed: {exception}");
            StatusText.Text = string.Format(texts.GUICrashResearchFailed, exception.Message);
            return;
        }

        // Replaces the shortlist's findings rather than adding to them: those were about mods that
        // have since been ruled out, and leaving them under a proved culprit reads as evidence
        // about the culprit.
        ForPanel.Children.Clear();
        AgainstPanel.Children.Clear();
        CautionPanel.Children.Clear();
        FindingsPanel.Children.Clear();
        LinksPanel.Children.Clear();

        ShowResearch(result);
    }

    private void RecordTrial(
        string crashKey, CrashSuspect? suspect, string? version, CrashTrialVerdict verdict, string note)
    {
        if (suspect is null) return;

        try
        {
            _context?.Trials?.Record(crashKey, suspect.ModId, version, verdict, note);
        }
        catch (Exception exception)
        {
            // Failing to write the verdict costs the user a repeated run. Throwing here would cost
            // them the run they have just spent four minutes on.
            Logger.Log($"Could not record a crash trial for {suspect.Name}: {exception.Message}");
        }
    }

    /// <summary>
    /// One line of progress, kept on screen.
    ///
    /// Every step also goes to AIM's log, so a user reporting "it disabled the mod and then nothing
    /// happened" hands over a log that says which of the four steps was the last one reached.
    /// </summary>
    private void Step(string message, bool? good = null)
    {
        Logger.Log($"Crash check: {message}");

        var line = new SelectableTextBlock
        {
            Text = $"{DateTime.Now:HH:mm:ss}  {message}",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = good is null ? 0.75 : 1
        };

        // Only the steps that carry a verdict get a colour. An ordinary step is left alone so it
        // inherits the theme's body colour rather than a fixed one that reads badly in one of them.
        if (good is not null) line.Foreground = good.Value ? Brushes.Green : Brushes.OrangeRed;

        VerifySteps.IsVisible = true;
        VerifySteps.Children.Add(line);
    }

    /// <summary>The verdict line under the step log. Null means "still going", not an answer.</summary>
    private void Say(bool? good, string message)
    {
        VerifyStatus.IsVisible = true;
        VerifyStatus.Text = message;
        VerifyStatus.Foreground = good switch
        {
            true => Brushes.Green,
            false => Brushes.OrangeRed,
            _ => Brushes.Gray
        };
    }

    private static void Report(TextBlock status, bool good, string message)
    {
        status.IsVisible = true;
        status.Text = message;
        status.Foreground = good ? Brushes.Green : Brushes.OrangeRed;
    }

    // ── What the mod pages say ───────────────────────────────────────────────────

    /// <summary>
    /// Reads the suspects' Nexus pages for this crash: bug tracker, comments, description, notes.
    ///
    /// The bug tracker matters more here than it does for a file conflict, because a crash is the
    /// kind of thing people report - so the question "has somebody already hit this" has a real
    /// chance of being answered yes, and answered with a fix or a version number.
    /// </summary>
    private async Task ResearchAsync(CrashDiagnosis diagnosis)
    {
        var texts = LocalizedTexts.Instance;

        // Only the ones worth spending a page fetch on. Reading the pages of eleven mods because
        // all eleven write to the same data set is a rate limit spent to tell the user nothing.
        var worth = diagnosis.Suspects
            .Where(suspect => suspect.Confidence >= CrashConfidence.Likely)
            .Take(4)
            .ToList();

        if (worth.Count == 0)
        {
            StatusText.Text = texts.GUICrashNothingToResearch;
            return;
        }

        var subjects = worth
            .Select(suspect =>
            {
                var provenance = _context?.Provenance(suspect.ModId);
                return new ResearchSubject(suspect.Name, provenance?.NexusModId, provenance?.PageUrl);
            })
            .ToList();

        ResearchResult result;

        try
        {
            result = await ConflictResearch.InvestigateAsync(subjects, _client, PageReader);
        }
        catch (Exception exception)
        {
            Logger.Log($"Crash research failed: {exception}");

            // The diagnosis stands on its own. A failed page read is a reason to show fewer quotes,
            // not a reason to withhold the answer AIM already worked out from the files.
            StatusText.Text = string.Format(texts.GUICrashResearchFailed, exception.Message);
            return;
        }

        ShowResearch(result);
    }

    /// <summary>Puts one research pass on screen, whether it covered the shortlist or one culprit.</summary>
    private void ShowResearch(ResearchResult result)
    {
        var texts = LocalizedTexts.Instance;

        StatusText.Text = _client is null
            ? texts.GUICrashNoApiKey
            : result.FoundNothing
                ? texts.GUICrashNothingFound
                : string.Format(texts.GUICrashFoundCount, result.Findings.Count);

        Fill(ForSection, ForPanel, result.Blockers, ForHeader, texts.GUICrashEvidenceFor);
        Fill(AgainstSection, AgainstPanel, result.Clearances, AgainstHeader, texts.GUICrashEvidenceAgainst);
        Fill(CautionSection, CautionPanel,
            result.Findings.Where(finding => finding.Polarity is Polarity.Caution),
            CautionHeader, texts.GUICrashEvidenceCaution);

        var rest = result.Findings.Where(finding => finding.Polarity is Polarity.Context).ToList();
        FindingsSection.IsVisible = rest.Count > 0;
        FindingsSection.Header = $"{texts.GUICrashEvidenceOther}  ({rest.Count})";
        foreach (var finding in rest) FindingsPanel.Children.Add(CreateFinding(finding));

        LinksSection.IsVisible = result.Links.Count > 0;

        foreach (var link in result.Links)
        {
            var button = new Button { Content = link.Label, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => ExternalUrl.Open(link.Url);
            ToolTip.SetTip(button, $"{link.Reason}\n{link.Url}");
            LinksPanel.Children.Add(button);
        }
    }

    private static void Fill(
        Control section,
        Panel panel,
        IEnumerable<ResearchFinding> findings,
        TextBlock header,
        string label)
    {
        var kept = findings.ToList();

        section.IsVisible = kept.Count > 0;
        header.Text = $"{label}  ({kept.Count})";

        foreach (var finding in kept) panel.Children.Add(CreateFinding(finding));
    }

    /// <summary>
    /// One thing a page said, with the phrase to find it by.
    ///
    /// Shorter than the conflict window's card on purpose. A crash produces more quotes than a file
    /// conflict does - people write about crashes - and the user reading this has already been
    /// given AIM's own answer above, so these are corroboration rather than the argument.
    /// </summary>
    private static Control CreateFinding(ResearchFinding finding)
    {
        var texts = LocalizedTexts.Instance;

        var accent = finding.Polarity switch
        {
            Polarity.Blocker => Color.Parse("#e05252"),
            Polarity.Clearance => Color.Parse("#3fa34d"),
            Polarity.Caution => Color.Parse("#d08a2a"),
            _ => Color.Parse("#8a8a8a")
        };

        var quote = new SelectableTextBlock
        {
            Text = $"“{finding.FullQuote}”",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };

        var where = string.IsNullOrEmpty(finding.Where) ? "" : $" · {finding.Where}";

        var source = new Button
        {
            Content = new TextBlock
            {
                Text = $"{finding.ModName} — {finding.Reason}{where}  ↗",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.7
            },
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand)
        };

        source.Click += (_, _) => ExternalUrl.Open(finding.SourceUrl);
        ToolTip.SetTip(source, $"{texts.GUICrashOpenPage}\n{finding.SourceUrl}");

        return new Border
        {
            Background = new SolidColorBrush(accent, 0.08),
            BorderThickness = new Avalonia.Thickness(3, 0, 0, 0),
            BorderBrush = new SolidColorBrush(accent),
            CornerRadius = new Avalonia.CornerRadius(0, 4, 4, 0),
            Padding = new Avalonia.Thickness(10, 6, 10, 6),
            Child = new StackPanel { Spacing = 3, Children = { quote, source } }
        };
    }
}
