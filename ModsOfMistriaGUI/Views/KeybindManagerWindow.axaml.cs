using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib;
using Garethp.ModsOfMistriaInstallerLib.Bindings;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// Every key and controller button the installed mods have bound, in one list.
///
/// Mods keep their settings in separate files under the game's own config directory, so answering
/// "what is F1 doing?" otherwise means opening a dozen JSON files by hand, or launching the game
/// and walking each mod's settings menu. The clash the conflict report can only warn about is
/// visible and fixable here.
/// </summary>
public partial class KeybindManagerWindow : Window
{
    private readonly IReadOnlyList<IMod> _mods;
    private readonly ModDataStore? _store;
    private readonly BindingVault? _vault;

    private List<ModBindingEntry> _entries = [];
    private Dictionary<ModBindingEntry, List<ModBindingEntry>> _overlaps = [];
    private bool _locked;

    public KeybindManagerWindow() : this([], null, null)
    {
    }

    private KeybindManagerWindow(IReadOnlyList<IMod> mods, ModDataStore? store, BindingVault? vault)
    {
        InitializeComponent();

        _mods = mods;
        _store = store;
        _vault = vault;

        var texts = LocalizedTexts.Instance;
        Title = texts.GUIKeybindsTitle;
        IntroText.Text = texts.GUIKeybindsIntro;
        OnlyOverlapsToggle.Content = texts.GUIKeybindsOnlyOverlaps;
        RefreshButton.Content = texts.GUIKeybindsRefresh;
        CloseButton.Content = texts.GUIClose;

        LockedBanner.Background = new SolidColorBrush(Color.FromArgb(40, 200, 140, 0));

        OnlyOverlapsToggle.IsCheckedChanged += (_, _) => Render();
        RefreshButton.Click += async (_, _) => await ReloadAsync();
        CloseButton.Click += (_, _) => Close();

        Opened += async (_, _) =>
        {
            // A dialog taller than the screen's working area is centred with its top edge
            // off the display, which puts the title bar out of reach. See DialogBounds.
            this.FitToScreen();
            await ReloadAsync();
        };
    }

    public static Task ShowAsync(
        Window owner, IReadOnlyList<IMod> mods, ModDataStore? store, BindingVault? vault) =>
        new KeybindManagerWindow(mods, store, vault).ShowDialog(owner);

    private static LocalizedTexts Texts => LocalizedTexts.Instance;

    // ── Loading ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads everything.
    ///
    /// The scan opens every settings file, and every <c>.gml</c> of every mod that has not written
    /// one, so it goes off the UI thread. Doing it inline froze the window for as long as the mod
    /// list was large - which is exactly when someone opens this.
    /// </summary>
    private async Task ReloadAsync()
    {
        RefreshButton.IsEnabled = false;
        SummaryText.Text = Texts.GUIKeybindsScanning;

        var mods = _mods;
        var store = _store;

        // This is reached from async void event handlers, so anything that escapes takes the whole
        // application with it rather than merely failing to refresh a list.
        try
        {
            var scan = await Task.Run(() =>
            {
                // The game rewrites every mod's settings when it exits, so an edit made while it is
                // running is thrown away without a trace. Read-only is the honest state, not a
                // limitation to apologise for.
                var running = GameProcess.IsRunning();
                var entries = BindingScanner.Scan(mods, store);
                return (Running: running, Entries: entries, Overlaps: BindingScanner.FindOverlaps(entries));
            });

            _locked = scan.Running;
            _entries = scan.Entries;
            _overlaps = scan.Overlaps;

            // Only claim the banner when there is something to say. A write failure puts its own
            // explanation here, and a routine refresh should not replace it with the running text.
            LockedBanner.IsVisible = _locked;
            if (_locked) LockedText.Text = Texts.GUIKeybindsGameRunning;

            // Seeing a binding is what makes it worth remembering: from here on AIM can tell when a
            // mod changes it back.
            _vault?.Remember(_entries);

            Render();
        }
        catch (Exception exception)
        {
            Logger.Log($"Reading mod keybinds failed: {exception}");
            LockedBanner.IsVisible = true;
            LockedText.Text = string.Format(Texts.GUIKeybindsScanFailed, exception.Message);
            SummaryText.Text = "";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    private void Render()
    {
        BindingsPanel.Children.Clear();

        var onlyOverlaps = OnlyOverlapsToggle.IsChecked == true;
        var shown = onlyOverlaps
            ? _entries.Where(entry => _overlaps.ContainsKey(entry)).ToList()
            : _entries;

        SummaryText.Text = _store is null
            ? Texts.GUIKeybindsNoConfigDirectory
            : string.Format(Texts.GUIKeybindsSummary, _entries.Count, _overlaps.Count);

        if (shown.Count == 0)
        {
            BindingsPanel.Children.Add(new TextBlock
            {
                Text = onlyOverlaps ? Texts.GUIKeybindsNoOverlaps : Texts.GUIKeybindsNone,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Margin = new Avalonia.Thickness(0, 8, 0, 0)
            });
            return;
        }

        string? lastMod = null;
        foreach (var entry in shown)
        {
            if (entry.ModName != lastMod)
            {
                BindingsPanel.Children.Add(new TextBlock
                {
                    Text = entry.ModName,
                    FontWeight = FontWeight.SemiBold,
                    Margin = new Avalonia.Thickness(0, 12, 0, 2)
                });
                lastMod = entry.ModName;
            }

            BindingsPanel.Children.Add(CreateRow(entry));
        }
    }

    // ── One binding ──────────────────────────────────────────────────────────────

    private Control CreateRow(ModBindingEntry entry)
    {
        var clashes = _overlaps.GetValueOrDefault(entry) ?? [];

        var setting = new TextBlock
        {
            Text = entry.FieldLabel,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Width = 260
        };

        var button = new Button
        {
            Content = entry.Value.Length == 0 ? Texts.GUIKeybindsUnbound : entry.Value,
            MinWidth = 190,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            IsEnabled = entry.IsEditable && !_locked
        };

        // Red is the whole point of the list: a clash has to be findable by scanning down the
        // column, without reading a word.
        if (clashes.Count > 0) button.Foreground = Brushes.IndianRed;

        ToolTip.SetTip(button, DescribeRow(entry, clashes));
        button.Click += async (_, _) => await EditAsync(entry);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Avalonia.Thickness(10, 1, 0, 1),
            Children = { setting, button }
        };

        var note = NoteFor(entry, clashes);
        if (note is not null)
        {
            row.Children.Add(new TextBlock
            {
                Text = note,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.75,
                Foreground = clashes.Count > 0 ? Brushes.IndianRed : null
            });
        }

        return row;
    }

    private string? NoteFor(ModBindingEntry entry, IReadOnlyList<ModBindingEntry> clashes)
    {
        if (clashes.Count > 0)
            return string.Format(Texts.GUIKeybindsAlsoUsedBy,
                string.Join(", ", clashes.Select(other => other.ModName).Distinct()));

        if (entry.Binding is null && entry.Value.Length > 0) return Texts.GUIKeybindsUnrecognised;
        if (entry.Source == BindingSource.ModDefault) return Texts.GUIKeybindsDefault;
        return null;
    }

    private string DescribeRow(ModBindingEntry entry, IReadOnlyList<ModBindingEntry> clashes)
    {
        var lines = new List<string> { $"{entry.ModName} — {entry.FieldLabel}" };

        if (clashes.Count > 0)
        {
            lines.Add("");
            lines.Add(Texts.GUIKeybindsClashHeader);
            lines.AddRange(clashes.Select(other =>
                $"  • {other.ModName} — {other.FieldLabel} ({other.Value})"));
        }

        if (entry.Source == BindingSource.ModDefault)
        {
            lines.Add("");
            lines.Add(Texts.GUIKeybindsDefaultTooltip);
        }
        else if (entry.File is not null)
        {
            lines.Add("");
            lines.Add(entry.File.Path);
        }

        if (entry.Binding is null && entry.Value.Length > 0)
        {
            lines.Add("");
            lines.Add(string.Format(Texts.GUIKeybindsUnrecognisedTooltip, entry.Value));
        }

        return string.Join("\n", lines);
    }

    private async Task EditAsync(ModBindingEntry entry)
    {
        if (entry.File is null || _locked) return;

        var chosen = await BindingEditorWindow.ShowAsync(
            this, $"{entry.ModName} — {entry.FieldLabel}", entry.Binding);
        if (chosen is null) return;

        if (!ModDataStore.WriteField(entry.File, entry.Field, chosen))
        {
            LockedBanner.IsVisible = true;
            LockedText.Text = string.Format(Texts.GUIKeybindsWriteFailed, entry.File.Path);
            return;
        }

        // Re-scan rather than patching the row: one edit can create or resolve a clash anywhere
        // else in the list, and a stale red row is worse than a moment's flicker.
        await ReloadAsync();
    }
}
