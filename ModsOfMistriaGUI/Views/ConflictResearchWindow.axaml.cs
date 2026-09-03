using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Nexus;
using Garethp.ModsOfMistriaInstallerLib.Research;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// "Find a fix" for one conflict.
///
/// Two mods writing the same file is reported because it might matter. The window answers that in
/// three passes, cheapest first, because the cheap one is usually enough:
///
///   1. It reads the files. What AIM's installer does with a shared file is not a matter of
///      opinion, so most of the time the question "is this actually a conflict?" has a definite
///      answer that needs no network, no API key and nobody's comment thread. That answer is at the
///      top of the window.
///   2. It reads the mods' Nexus pages - description, changelog, files, comments and bug tracker -
///      and sorts what it finds by which way it points, so a sentence clearing the pairing is not
///      buried among twenty that merely mention the word "compatible".
///   3. It offers to do something. Reordering, installing a patch somebody has already written, or
///      setting one mod's copy of the file aside - each spelled out in terms of what changes, and
///      each applied only when the user picks it.
///
/// Then it takes down the answer, so nobody has to work it out twice.
/// </summary>
public partial class ConflictResearchWindow : Window
{
    private readonly IReadOnlyList<ResearchSubject> _subjects;
    private readonly NexusApiClient? _client;
    private readonly ResearchContext? _context;

    private IssueVerdict? _result;
    private ConflictDiagnosis? _diagnosis;

    /// <summary>
    /// Used only for reading mod pages Nexus has no API for. Shared and short-timeout: a page that
    /// hangs should stop being waited on, not stall the whole window.
    /// </summary>
    private static readonly HttpClient PageReader = new() { Timeout = TimeSpan.FromSeconds(20) };

    // Required by Avalonia's compiled XAML loader; normal callers use ShowAsync below.
    public ConflictResearchWindow() : this("", [], null, null)
    {
    }

    private ConflictResearchWindow(
        string issue,
        IReadOnlyList<ResearchSubject> subjects,
        NexusApiClient? client,
        ResearchContext? context)
    {
        InitializeComponent();

        _subjects = subjects;
        _client = client;
        _context = context;

        var texts = LocalizedTexts.Instance;
        Title = texts.GUIResearchTitle;
        IssueText.Text = issue;
        FindingsHeader.Text = texts.GUIResearchEvidenceOther;
        AgainstHeader.Text = texts.GUIResearchEvidenceAgainst;
        ForHeader.Text = texts.GUIResearchEvidenceFor;
        DiagnosisHeader.Text = texts.GUIResearchDiagnosisHeader;
        FixesHeader.Text = texts.GUIResearchFixesHeader;
        FixesHint.Text = texts.GUIResearchFixesHint;
        PatchesHeader.Text = texts.GUIResearchPatchesHeader;
        LinksHeader.Text = texts.GUIResearchLinksHeader;
        LinksHint.Text = texts.GUIResearchLinksHint;
        VerdictHeader.Text = texts.GUIResearchVerdictHeader;
        VerdictHint.Text = texts.GUIResearchVerdictHint;
        PatchLinkLabel.Text = texts.GUIResearchPatchLinkLabel;
        NotAnIssueButton.Content = texts.GUIResearchNotAnIssue;
        PatchButton.Content = texts.GUIResearchPatchExists;
        IncompatibleButton.Content = texts.GUIResearchIncompatible;
        CancelButton.Content = texts.GUIResearchUndecided;

        ToolTip.SetTip(NotAnIssueButton, texts.GUIResearchNotAnIssueTooltip);
        ToolTip.SetTip(PatchButton, texts.GUIResearchPatchExistsTooltip);
        ToolTip.SetTip(IncompatibleButton, texts.GUIResearchIncompatibleTooltip);

        NotAnIssueButton.Click += (_, _) => Finish(new IssueVerdict(DismissedIssueStore.VerdictNotAnIssue));
        IncompatibleButton.Click += (_, _) => Finish(new IssueVerdict(DismissedIssueStore.VerdictIncompatible));
        CancelButton.Click += (_, _) => Finish(null);

        // The link is what makes "a patch exists" useful a month later, so ask for it before
        // accepting the verdict rather than filing an answer nobody can act on.
        PatchButton.Click += (_, _) => RecordPatch();

        // Typing a URL and pressing Enter is the natural gesture, and it would otherwise fire the
        // default button - which is Cancel - and throw the answer away.
        PatchLinkBox.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            RecordPatch();
        };

        Opened += async (_, _) =>
        {
            // Before anything is laid out: a dialog taller than the screen's working area gets
            // centred with its top edge off the top of the display, which puts the title bar and
            // the first controls out of reach. See DialogBounds.
            this.FitToScreen();

            await InvestigateAsync();
        };
    }

    /// <summary>
    /// First press reveals the link box and makes it the default action; second press files the
    /// verdict. A link that is not https is refused rather than stored, because the report can only
    /// offer to open https ones and a silently unusable link is worse than none.
    /// </summary>
    private void RecordPatch()
    {
        if (!PatchLinkRow.IsVisible)
        {
            PatchLinkRow.IsVisible = true;
            PatchButton.Content = LocalizedTexts.Instance.GUIResearchPatchConfirm;
            PatchButton.IsDefault = true;
            CancelButton.IsDefault = false;
            PatchLinkBox.Focus();
            return;
        }

        var link = PatchLinkBox.Text?.Trim();
        if (!string.IsNullOrEmpty(link) && !ExternalUrl.IsAllowed(link))
        {
            // Its own label, not the status line: the research scan writes there and would wipe
            // this out the moment it finished.
            PatchLinkError.Text = LocalizedTexts.Instance.GUIResearchPatchLinkInvalid;
            PatchLinkError.IsVisible = true;
            PatchLinkBox.Focus();
            return;
        }

        PatchLinkError.IsVisible = false;

        Finish(new IssueVerdict(
            DismissedIssueStore.VerdictPatchExists,
            string.IsNullOrEmpty(link) ? null : link));
    }

    /// <summary>The conclusion the user recorded, or null if they left it undecided.</summary>
    public static async Task<IssueVerdict?> ShowAsync(
        Window owner,
        string issue,
        IReadOnlyList<ResearchSubject> subjects,
        NexusApiClient? client,
        ResearchContext? context = null)
    {
        var window = new ConflictResearchWindow(issue, subjects, client, context);
        await window.ShowDialog(owner);
        return window._result;
    }

    private void Finish(IssueVerdict? verdict)
    {
        _result = verdict;
        Close();
    }

    // ── Working out the answer ───────────────────────────────────────────────────

    private async Task InvestigateAsync()
    {
        var texts = LocalizedTexts.Instance;

        // The file diagnosis first, and on its own thread. It reads and parses every shared file,
        // which is fast but not instant, and it is the part that works with no network at all -
        // so it must not be made to wait behind a page fetch that may time out.
        if (_context is not null && _context.Mods.Count > 1)
        {
            _diagnosis = await Task.Run(() =>
                ConflictDiagnoser.Diagnose(_context.SharedPaths, _context.Mods));

            ShowDiagnosis(_diagnosis);
        }

        StatusText.Text = _client is null ? texts.GUIResearchNoApiKey : texts.GUIResearchWorking;

        ResearchResult result;
        try
        {
            result = await ConflictResearch.InvestigateAsync(_subjects, _client, PageReader);
        }
        catch (Exception exception)
        {
            Logger.Log($"Conflict research failed: {exception}");
            StatusText.Text = string.Format(texts.GUIResearchFailed, exception.Message);

            // The diagnosis stands on its own. A failed page read is a reason to show fewer
            // quotes, not a reason to withhold the answer AIM already has.
            ShowFixes(result: null);
            return;
        }

        StatusText.Text = _client is null
            ? texts.GUIResearchNoApiKey
            : result.FoundNothing
                ? texts.GUIResearchNothingFound
                : string.Format(texts.GUIResearchFoundCount, result.Findings.Count);

        ShowEvidence(result);
        ShowPatches(result);
        ShowFixes(result);

        LinksSection.IsVisible = result.Links.Count > 0;
        foreach (var link in result.Links)
            LinksPanel.Children.Add(CreateLink(link));
    }

    // ── The diagnosis ────────────────────────────────────────────────────────────

    private void ShowDiagnosis(ConflictDiagnosis diagnosis)
    {
        var texts = LocalizedTexts.Instance;

        DiagnosisSection.IsVisible = true;
        DiagnosisHeadline.Text = diagnosis.Headline;

        DiagnosisSection.BorderBrush = diagnosis.Verdict switch
        {
            DiagnosisVerdict.Harmless => new SolidColorBrush(Color.Parse("#2e7d32")),
            DiagnosisVerdict.OrderDecides => new SolidColorBrush(Color.Parse("#b26a00")),
            DiagnosisVerdict.PartialOverride => new SolidColorBrush(Color.Parse("#b26a00")),
            _ => new SolidColorBrush(Color.Parse("#757575"))
        };

        foreach (var reason in diagnosis.Reasons)
            DiagnosisReasons.Children.Add(new TextBlock
            {
                Text = "• " + reason,
                TextWrapping = TextWrapping.Wrap
            });

        DiagnosisCertainty.Text = diagnosis.Certain
            ? texts.GUIResearchDiagnosisCertain
            : texts.GUIResearchDiagnosisUncertain;

        DiagnosisFilesExpander.IsVisible = diagnosis.Files.Count > 0;
        DiagnosisFilesExpander.Header = $"{diagnosis.Files.Count} shared " +
                                        (diagnosis.Files.Count == 1 ? "file" : "files");

        foreach (var file in diagnosis.Files)
            DiagnosisFiles.Children.Add(CreateFileVerdict(file));
    }

    private static Control CreateFileVerdict(FileVerdict file)
    {
        var lines = new StackPanel
        {
            Children =
            {
                new SelectableTextBlock { Text = file.Path, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = file.Explanation, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 }
            }
        };

        // Naming the keys is the difference between "these files conflict" and an answer the user
        // can act on: it is usually two or three settings out of a hundred, and seeing which ones
        // often settles the question on the spot.
        if (file.ContestedKeys.Count > 0)
            lines.Children.Add(new SelectableTextBlock
            {
                Text = string.Join(", ", file.ContestedKeys.Take(12)) +
                       (file.ContestedKeys.Count > 12 ? $", and {file.ContestedKeys.Count - 12} more" : ""),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                FontFamily = FontFamily.Parse("Consolas, Menlo, monospace")
            });

        return new Border
        {
            BorderThickness = new Avalonia.Thickness(2, 0, 0, 0),
            BorderBrush = file.Outcome switch
            {
                FileOutcome.Identical or FileOutcome.MergesCleanly => Brushes.Green,
                FileOutcome.LastWins or FileOutcome.MergesWithOverride => Brushes.Orange,
                _ => Brushes.Gray
            },
            Padding = new Avalonia.Thickness(10, 2, 0, 2),
            Child = lines
        };
    }

    // ── What can be done about it ────────────────────────────────────────────────

    private void ShowFixes(ResearchResult? result)
    {
        if (_context is null) return;

        var diagnosis = _diagnosis ?? ConflictDiagnosis.Inconclusive("AIM did not read the files.");
        var mods = _context.Mods.Select(mod => (mod.GetId(), mod.GetName())).ToList();

        var plans = FixPlanner.Plan(diagnosis, result?.Patches ?? [], mods);

        FixesPanel.Children.Clear();
        foreach (var plan in plans) FixesPanel.Children.Add(CreateFix(plan));

        FixesSection.IsVisible = plans.Count > 0;
    }

    private Control CreateFix(FixPlan plan)
    {
        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            IsVisible = false,
            Margin = new Avalonia.Thickness(0, 4, 0, 0)
        };

        var apply = new Button
        {
            Content = LocalizedTexts.Instance.GUIResearchApply,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 6, 0, 0)
        };

        apply.Click += async (_, _) => await ApplyAsync(plan, apply, status);

        return new Border
        {
            BorderThickness = new Avalonia.Thickness(1),
            BorderBrush = Brushes.Gray,
            CornerRadius = new Avalonia.CornerRadius(6),
            Padding = new Avalonia.Thickness(12),
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = plan.Title, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = plan.Consequence, TextWrapping = TextWrapping.Wrap, Opacity = 0.8 },
                    apply,
                    status
                }
            }
        };
    }

    private async Task ApplyAsync(FixPlan plan, Button button, TextBlock status)
    {
        if (_context is null) return;

        button.IsEnabled = false;
        status.IsVisible = true;
        status.Foreground = Brushes.Gray;
        status.Text = "…";

        try
        {
            switch (plan.Kind)
            {
                case FixKind.CloseAsHarmless:
                    // The diagnosis is the note worth keeping: it says *why* this was closed, in a
                    // form that still means something when the issue resurfaces after an update.
                    Finish(new IssueVerdict(
                        DismissedIssueStore.VerdictNotAnIssue,
                        Note: _diagnosis?.Headline));
                    return;

                case FixKind.Reorder when plan.WinnerModId is not null:
                    if (_context.MakeWin(plan.WinnerModId))
                        Finish(new IssueVerdict(DismissedIssueStore.VerdictNotAnIssue, Note: plan.Title));
                    else
                        Report(status, (false, "The mods could not be reordered from here."));
                    return;

                case FixKind.InstallPatch when plan.Patch is not null:
                    var failure = await _context.InstallPatch(plan.Patch);

                    // A message here is not necessarily a failure - a free Nexus account is told to
                    // finish the download from the page that just opened - but either way the issue
                    // is not resolved yet, so the window stays put with the reason on screen.
                    if (failure is null)
                        Finish(new IssueVerdict(
                            DismissedIssueStore.VerdictPatchExists, plan.Patch.Url, plan.Title));
                    else
                        Report(status, (false, failure));
                    return;

                case FixKind.SetAsideFile when plan.TargetModId is not null:
                    await SetAsideAsync(plan, button, status);
                    return;

                default:
                    ExternalUrl.Open(ConflictResearch.WebSearchUrl(_subjects));
                    status.IsVisible = false;
                    return;
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"Applying a fix failed: {exception}");
            Report(status, (false, exception.Message));
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    /// <summary>
    /// The one action that changes somebody else's mod, and the only one behind a confirmation.
    ///
    /// Everything else here is a preference AIM already owns - the order of its own list, a mod it
    /// downloads. Renaming a file inside a mod folder is not, so the user is told what it does,
    /// where the untouched copy goes, and that the row will be marked, before it happens.
    /// </summary>
    private async Task SetAsideAsync(FixPlan plan, Button button, TextBlock status)
    {
        var setAside = _context?.SetAside;

        if (setAside is null)
        {
            Report(status, (false, "AIM has nowhere to keep a backup, so it will not edit the mod."));
            return;
        }

        var confirm = await MessageBoxManager.GetMessageBoxStandard(
            plan.Title,
            $"{plan.Consequence}\n\n{LocalizedTexts.Instance.GUIResearchSetAsideWarning}",
            ButtonEnum.YesNo).ShowAsync();

        if (confirm != ButtonResult.Yes)
        {
            status.IsVisible = false;
            button.IsEnabled = true;
            return;
        }

        var outcome = await setAside(plan.TargetModId!, plan.TargetFiles, plan.Title);

        if (!outcome.Applied)
        {
            Report(status, (false, outcome.Message));
            return;
        }

        // An applied edit is itself the answer to the issue, and one worth being able to find
        // again - so it is filed with what AIM did rather than left as a dismissal with no
        // explanation.
        Finish(new IssueVerdict(DismissedIssueStore.VerdictNotAnIssue, Note: outcome.Message));
    }

    /// <summary>
    /// Says why a fix did not happen, and leaves the window open so it can be read.
    ///
    /// There is no success counterpart: applying a fix closes the window and hands the report a
    /// verdict, so the confirmation the user sees is the issue itself moving to the resolved list
    /// with the reason attached. A "done" line on a window that is about to disappear would be
    /// read by nobody.
    /// </summary>
    private static void Report(TextBlock status, (bool Ok, string Message) outcome)
    {
        status.IsVisible = true;
        status.Foreground = Brushes.OrangeRed;
        status.Text = string.Format(LocalizedTexts.Instance.GUIResearchApplyFailed, outcome.Message);
    }

    // ── What the pages say ───────────────────────────────────────────────────────

    private void ShowEvidence(ResearchResult result)
    {
        Fill(AgainstSection, AgainstPanel, result.Clearances);
        Fill(ForSection, ForPanel, result.Blockers);
        Fill(FindingsSection, FindingsPanel, result.Findings.Where(finding =>
            finding.Polarity is not Polarity.Clearance and not Polarity.Blocker));
    }

    private static void Fill(Control section, Panel panel, IEnumerable<ResearchFinding> findings)
    {
        var kept = findings.ToList();
        section.IsVisible = kept.Count > 0;

        foreach (var finding in kept) panel.Children.Add(CreateFinding(finding));
    }

    private void ShowPatches(ResearchResult result)
    {
        PatchesSection.IsVisible = result.Patches.Count > 0;

        foreach (var patch in result.Patches)
        {
            var open = new Button { Content = patch.Title, HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += (_, _) => ExternalUrl.Open(patch.Url);
            ToolTip.SetTip(open, patch.Url);

            PatchesPanel.Children.Add(new StackPanel
            {
                Children =
                {
                    open,
                    new TextBlock { Text = patch.Why, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 }
                }
            });
        }
    }

    private static Control CreateFinding(ResearchFinding finding)
    {
        var quote = new SelectableTextBlock
        {
            Text = $"“{finding.Quote}”",
            TextWrapping = TextWrapping.Wrap
        };

        var where = string.IsNullOrEmpty(finding.Where) ? "" : $", in the {finding.Where}";

        var attribution = new TextBlock
        {
            Text = $"— {finding.ModName}, {finding.Reason}{where}",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Avalonia.Thickness(0, 2, 0, 0)
        };

        var open = new Button
        {
            Content = LocalizedTexts.Instance.GUIResearchOpenPage,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Avalonia.Thickness(0, 6, 0, 0)
        };
        open.Click += (_, _) => ExternalUrl.Open(finding.SourceUrl);

        return new Border
        {
            BorderThickness = new Avalonia.Thickness(2, 0, 0, 0),
            BorderBrush = finding.Polarity switch
            {
                Polarity.Clearance => Brushes.Green,
                Polarity.Blocker => Brushes.OrangeRed,
                _ => Brushes.Gray
            },
            Padding = new Avalonia.Thickness(10, 2, 0, 2),
            Child = new StackPanel { Children = { quote, attribution, open } }
        };
    }

    private static Control CreateLink(ResearchLink link)
    {
        var button = new Button
        {
            Content = link.Label,
            HorizontalAlignment = HorizontalAlignment.Left,
            MinWidth = 240
        };
        button.Click += (_, _) => ExternalUrl.Open(link.Url);
        ToolTip.SetTip(button, $"{link.Reason}\n{link.Url}");

        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children =
            {
                button,
                new TextBlock
                {
                    Text = link.Reason,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.75
                }
            }
        };
    }
}
