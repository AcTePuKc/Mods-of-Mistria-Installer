using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
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
    private IReadOnlyList<InstalledFix> _installed = [];

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
        // The evidence headers carry a count, so they are written when there is something to count.
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

        ScrollEnds.Attach(BodyScroller);

        Opened += async (_, _) =>
        {
            // Before anything is laid out: a dialog taller than the screen's working area gets
            // centred with its top edge off the top of the display, which puts the title bar and
            // the first controls out of reach. See DialogBounds.
            this.FitToScreen();

            // Nothing may escape this handler. It is an async void by the event's own signature,
            // so an exception thrown past it is not caught anywhere and takes the application down
            // - and everything below reads somebody else's mod files off a disk. Losing a section
            // of this window is a bad outcome; losing the user's session over it is a worse one.
            try
            {
                // The mods themselves are known before any file is read or any page is fetched, so
                // they go up immediately rather than waiting behind a call that may time out.
                ShowMods();

                await InvestigateAsync();
            }
            catch (Exception exception)
            {
                Logger.Log($"The research window could not finish: {exception}");

                // Not the "could not read the mod pages" message: what fails here is as likely to
                // be the file diagnosis or the scan of the mod list, and blaming Nexus for a disk
                // error sends the user off to check the wrong thing.
                StatusText.Text = string.Format(
                    LocalizedTexts.Instance.GUIResearchWindowFailed, exception.Message);
            }
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
            await ShowFixes(result: null);
            return;
        }

        StatusText.Text = _client is null
            ? texts.GUIResearchNoApiKey
            : result.FoundNothing
                ? texts.GUIResearchNothingFound
                : string.Format(texts.GUIResearchFoundCount, result.Findings.Count);

        ShowEvidence(result);

        // The scan runs inside ShowFixes and decides which patches are worth listing at all, so it
        // has to happen before the patch list is drawn rather than after it.
        await ShowFixes(result);
        ShowPatches(result);

        LinksSection.IsVisible = result.Links.Count > 0;
        foreach (var link in result.Links)
            LinksPanel.Children.Add(CreateLink(link));
    }

    // ── The mods in the conflict ─────────────────────────────────────────────────

    /// <summary>
    /// One row per mod, with its page and a way to remove it.
    ///
    /// Both are things the window was implicitly telling the user to go and do elsewhere. AIM's
    /// answer to a conflict is always some arrangement of two mods it assumes you want to keep, and
    /// often the real answer is "I only installed that to try it" - which meant closing this
    /// window, finding the row in the list, and remembering which of the two it was. The page is
    /// the same problem from the other side: the screenshots are how somebody decides which of two
    /// portrait mods they actually want, and the links at the bottom of this window are a search,
    /// not the mod.
    /// </summary>
    private void ShowMods()
    {
        if (_context is null || _context.Mods.Count == 0) return;

        var texts = LocalizedTexts.Instance;
        ModsHeader.Text = texts.GUIResearchModsHeader;
        ModsHint.Text = texts.GUIResearchModsHint;
        ModsPanel.Children.Clear();

        foreach (var mod in _context.Mods) ModsPanel.Children.Add(CreateModRow(mod));

        ModsSection.IsVisible = true;
    }

    private Control CreateModRow(IMod mod)
    {
        var texts = LocalizedTexts.Instance;

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                new SelectableTextBlock
                {
                    Text = mod.GetName(),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };

        var page = PageFor(mod);

        var open = new Button { Content = texts.GUIResearchOpenModPage, IsEnabled = page is not null };
        ToolTip.SetTip(open, page ?? texts.GUIResearchOpenPageMissing);
        if (page is not null) open.Click += (_, _) => ExternalUrl.Open(page);
        row.Children.Add(open);

        // No remove button at all when the list cannot act on it, rather than one that explains
        // itself only after being pressed. Held in a local so the lambda does not have to reason
        // about a field that could, as far as the compiler knows, have changed by the time it runs.
        var removeMod = _context?.RemoveMod;

        if (removeMod is not null)
        {
            var remove = new Button { Content = texts.GUIResearchRemoveMod };
            ToolTip.SetTip(remove, texts.GUIResearchRemoveModTooltip);

            remove.Click += async (_, _) =>
            {
                remove.IsEnabled = false;
                try
                {
                    // The mod list owns the confirmation, the recycle bin and the Nexus
                    // bookkeeping; this only asks for it and reacts to the answer.
                    if (await removeMod(mod.GetId()))
                        Finish(new IssueVerdict(
                            DismissedIssueStore.VerdictNotAnIssue,
                            Note: string.Format(texts.GUIResearchRemovedNote, mod.GetName())));
                }
                catch (Exception exception)
                {
                    // Same reasoning as the Opened handler: this is an async void, so a failure
                    // that escapes it is not caught anywhere.
                    Logger.Log($"Removing {mod.GetName()} from the research window failed: {exception}");
                }
                finally
                {
                    remove.IsEnabled = true;
                }
            };

            row.Children.Add(remove);
        }

        return row;
    }

    /// <summary>
    /// Where this mod lives on Nexus, from whichever of the window's two views of the mod list
    /// knows: the research subjects carry a page for the mods AIM has provenance for, and the
    /// installed snapshot carries one for the rest.
    /// </summary>
    private string? PageFor(IMod mod)
    {
        var installed = _context?.Installed
            .FirstOrDefault(view => string.Equals(
                view.Mod.GetSourcePath(), mod.GetSourcePath(), StringComparison.OrdinalIgnoreCase));

        var candidate = installed?.PageUrl
                        ?? _subjects
                            .FirstOrDefault(subject => subject.Name == mod.GetName())?.PageUrl
                        ?? mod.GetDownloadUrl();

        return candidate is not null && ExternalUrl.IsAllowed(candidate) ? candidate : null;
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

    private async Task ShowFixes(ResearchResult? result)
    {
        if (_context is null) return;

        var diagnosis = _diagnosis ?? ConflictDiagnosis.Inconclusive("AIM did not read the files.");
        var mods = _context.Mods.Select(mod => (mod.GetId(), mod.GetName())).ToList();

        // Before anything is offered for download: what does the user already have?
        //
        // Off the UI thread for the same reason the diagnosis is. It is one existence check per
        // installed mod per shared file, and for an archived mod that means walking the archive's
        // entries - which on a long list and a seventy-four-file conflict would freeze the window.
        var patches = result?.Patches ?? [];
        var context = _context;

        _installed = await Task.Run(() => InstalledFixScanner.Scan(
            context.Installed, context.Mods, context.SharedPaths, patches));

        ShowInstalled(_installed);

        var plans = FixPlanner.Plan(diagnosis, patches, mods, _installed);

        FixesPanel.Children.Clear();
        foreach (var plan in plans) FixesPanel.Children.Add(CreateFix(plan));

        FixesSection.IsVisible = plans.Count > 0;
    }

    /// <summary>
    /// The mods the user already has that bear on this conflict, listed whether or not any of them
    /// turned into a plan - a third mod writing the same files does not fix anything, but it is
    /// part of the answer to "what is actually happening to these files", and the user cannot see
    /// it from the conflict report.
    /// </summary>
    private void ShowInstalled(IReadOnlyList<InstalledFix> found)
    {
        InstalledPanel.Children.Clear();
        InstalledSection.IsVisible = found.Count > 0;
        if (found.Count == 0) return;

        var texts = LocalizedTexts.Instance;
        InstalledHeader.Text = texts.GUIResearchInstalledHeader;
        InstalledHint.Text = texts.GUIResearchInstalledHint;

        foreach (var fix in found)
        {
            var state = fix.Evidence == FixEvidence.WritesTheSameFiles
                ? ""
                : fix.Effective
                    ? " It is switched on and loads last, so it is in effect."
                    : !fix.Enabled
                        ? " It is switched off, so it is not doing anything."
                        : " It loads before the mods it patches, so it is not doing anything.";

            InstalledPanel.Children.Add(new Border
            {
                BorderThickness = new Avalonia.Thickness(2, 0, 0, 0),
                BorderBrush = fix.Evidence == FixEvidence.WritesTheSameFiles
                    ? Brushes.Gray
                    : fix.Effective
                        ? Brushes.Green
                        : Brushes.Orange,
                Padding = new Avalonia.Thickness(10, 2, 0, 2),
                Child = new StackPanel
                {
                    Children =
                    {
                        new SelectableTextBlock
                        {
                            Text = fix.Name,
                            FontWeight = FontWeight.SemiBold,
                            TextWrapping = TextWrapping.Wrap
                        },
                        new TextBlock
                        {
                            Text = fix.Why + state,
                            TextWrapping = TextWrapping.Wrap,
                            Opacity = 0.8
                        }
                    }
                }
            });
        }
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

                case FixKind.AlreadyFixed when plan.ExistingFix is not null:
                    // Filed as "a patch exists" rather than "not an issue", with the patch named:
                    // that is the verdict that still means something when the issue resurfaces,
                    // because it says what is holding the pairing together.
                    Finish(new IssueVerdict(
                        DismissedIssueStore.VerdictPatchExists,
                        plan.ExistingFix.PageUrl,
                        $"Already handled by {plan.ExistingFix.Name}."));
                    return;

                case FixKind.UseExistingFix when plan.ExistingFix is not null:
                    var used = UseExisting(plan.ExistingFix);
                    if (used.Ok)
                        Finish(new IssueVerdict(
                            DismissedIssueStore.VerdictPatchExists,
                            plan.ExistingFix.PageUrl,
                            used.Message));
                    else
                        Report(status, used);
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
    /// Puts a patch the user already has into a position to work: switched on, and below the mods
    /// it patches.
    ///
    /// Both steps are attempted even when only one looked wrong, because the list may have moved
    /// since the scan, and both are cheap and idempotent. The result says what was done, so the
    /// verdict filed against the issue records the reason rather than just the outcome.
    /// </summary>
    private (bool Ok, string Message) UseExisting(InstalledFix fix)
    {
        if (_context is null) return (false, "The mod list is not available from here.");

        var did = new List<string>();

        if (!fix.Enabled)
        {
            if (_context.Enable is null || !_context.Enable(fix.ModId))
                return (false, $"{fix.Name} could not be switched on from here.");

            did.Add("switched on");
        }

        if (!fix.LoadsLast)
        {
            if (!_context.MakeWin(fix.ModId))
                return (false, $"{fix.Name} could not be moved below the other mods from here.");

            did.Add("moved below both mods");
        }

        return did.Count == 0
            ? (true, $"{fix.Name} was already in place.")
            : (true, $"{fix.Name} {string.Join(" and ", did)}.");
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

    /// <summary>
    /// The four answers a page can give, each in its own place and its own colour.
    ///
    /// Blockers first: a user who is about to dismiss an issue needs the reason not to before the
    /// reasons to. Then the all-clears, then the conditions, and last - shut - everything that
    /// merely touches on the subject.
    /// </summary>
    private void ShowEvidence(ResearchResult result)
    {
        var texts = LocalizedTexts.Instance;

        Fill(ForSection, ForPanel, result.Blockers, ForHeader, texts.GUIResearchEvidenceFor);
        Fill(AgainstSection, AgainstPanel, result.Clearances, AgainstHeader, texts.GUIResearchEvidenceAgainst);

        Fill(CautionSection, CautionPanel,
            result.Findings.Where(finding => finding.Polarity is Polarity.Caution),
            CautionHeader, texts.GUIResearchEvidenceCaution);

        var context = result.Findings.Where(finding => finding.Polarity is Polarity.Context).ToList();
        FindingsSection.IsVisible = context.Count > 0;
        FindingsSection.Header = Count(texts.GUIResearchEvidenceOther, context.Count);
        foreach (var finding in context) FindingsPanel.Children.Add(CreateFinding(finding));
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
        header.Text = Count(label, kept.Count);

        foreach (var finding in kept) panel.Children.Add(CreateFinding(finding));
    }

    /// <summary>A heading that says how much is under it, so a shut section still tells you.</summary>
    private static string Count(string label, int count) => $"{label}  ({count})";

    private void ShowPatches(ResearchResult result)
    {
        // "Patches that already exist" is a list of things to go and get. One the user already has
        // does not belong on it - and listing it directly under a section headed "what you already
        // have" reads as a contradiction.
        var missing = result.Patches
            .Except(InstalledFixScanner.AlreadyHave(_installed, result.Patches))
            .ToList();

        PatchesSection.IsVisible = missing.Count > 0;

        foreach (var patch in missing)
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

    /// <summary>
    /// The colour and the one-word verdict for each kind of finding.
    ///
    /// The tint is the accent at low alpha rather than a fixed pale colour, so it reads as a tint on
    /// a dark window and on a light one without either being written down twice.
    /// </summary>
    private static (Color Accent, string Badge) Style(Polarity polarity)
    {
        var texts = LocalizedTexts.Instance;

        return polarity switch
        {
            Polarity.Blocker => (Color.Parse("#e05252"), texts.GUIResearchBadgeBlocker),
            Polarity.Clearance => (Color.Parse("#3fa34d"), texts.GUIResearchBadgeClearance),
            Polarity.Caution => (Color.Parse("#d08a2a"), texts.GUIResearchBadgeCaution),
            _ => (Color.Parse("#8a8a8a"), texts.GUIResearchBadgeContext)
        };
    }

    /// <summary>
    /// One piece of evidence, in three lines at most: what it is, what it said, and where it came
    /// from.
    ///
    /// It used to be five - a quote of up to three hundred characters, a full attribution sentence,
    /// and a wide button repeating "Open this mod's page" under every single one - so four findings
    /// filled the window and the user had to read all of it to sort the useful from the incidental.
    /// The verdict is now a coloured badge that can be taken in without reading, the source line is
    /// small and grey, and the link is part of that line instead of a control of its own.
    /// </summary>
    private static Control CreateFinding(ResearchFinding finding)
    {
        var (accent, badgeText) = Style(finding.Polarity);

        var badge = new Border
        {
            Background = new SolidColorBrush(accent),
            CornerRadius = new Avalonia.CornerRadius(3),
            Padding = new Avalonia.Thickness(6, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = badgeText,
                FontSize = 10,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White
            }
        };

        var heading = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children =
            {
                badge,
                new TextBlock
                {
                    Text = finding.ModName,
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 12,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };

        var quote = new SelectableTextBlock
        {
            Text = $"“{finding.Quote}”",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13
        };

        // The whole source line opens the page. A link is what this is, so it looks like one rather
        // than like a button that happens to be under every finding.
        var where = string.IsNullOrEmpty(finding.Where) ? "" : $" · {finding.Where}";

        var source = new Button
        {
            Content = new TextBlock
            {
                Text = $"{finding.Reason}{where}  ↗",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
                Opacity = 0.7
            },
            Background = Brushes.Transparent,
            BorderThickness = new Avalonia.Thickness(0),
            Padding = new Avalonia.Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Left,
            Cursor = new Cursor(StandardCursorType.Hand)
        };

        source.Click += (_, _) => ExternalUrl.Open(finding.SourceUrl);
        ToolTip.SetTip(source, $"{LocalizedTexts.Instance.GUIResearchOpenPage}\n{finding.SourceUrl}");

        return new Border
        {
            Background = new SolidColorBrush(accent, 0.08),
            BorderThickness = new Avalonia.Thickness(3, 0, 0, 0),
            BorderBrush = new SolidColorBrush(accent),
            CornerRadius = new Avalonia.CornerRadius(0, 4, 4, 0),
            Padding = new Avalonia.Thickness(10, 6, 10, 6),
            Child = new StackPanel { Spacing = 3, Children = { heading, quote, source } }
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
