using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib.Nexus;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// A mod's release notes, every version the author wrote them for, newest first.
///
/// Each version gets a heading and its own notes beneath, because the question this answers is
/// usually "what changed since the copy I had?" - which needs several versions read in order, not
/// just the newest one.
/// </summary>
public partial class ChangelogWindow : Window
{
    private readonly string _plainText;

    // Required by Avalonia's compiled XAML loader; normal callers use ShowAsync.
    public ChangelogWindow() : this("", null)
    {
    }

    private ChangelogWindow(string modName, IReadOnlyList<ModChangelogEntry>? entries)
    {
        InitializeComponent();

        // A dialog taller than the screen's working area is centred with its top edge off
        // the display, which puts the title bar out of reach. See DialogBounds.
        Opened += (_, _) => this.FitToScreen();

        var texts = LocalizedTexts.Instance;
        Title = texts.GUIChangelogTitle;
        ModNameText.Text = modName;
        CopyButton.Content = texts.GUICopyReport;
        CloseButton.Content = texts.GUIClose;

        _plainText = BuildPlainText(modName, entries);

        CopyButton.IsVisible = entries is { Count: > 0 };
        CopyButton.Click += async (_, _) =>
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard is not null) await clipboard.SetTextAsync(_plainText);
        };
        CloseButton.Click += (_, _) => Close();

        if (entries is null)
        {
            StatusText.Text = texts.GUIChangelogUnavailable;
            return;
        }

        if (entries.Count == 0)
        {
            StatusText.Text = texts.GUIChangelogNone;
            return;
        }

        StatusText.Text = string.Format(texts.GUIChangelogVersionCount, entries.Count);
        foreach (var entry in entries) VersionsPanel.Children.Add(CreateVersion(entry));
    }

    public static Task ShowAsync(Window owner, string modName, IReadOnlyList<ModChangelogEntry>? entries) =>
        new ChangelogWindow(modName, entries).ShowDialog(owner);

    /// <summary>
    /// One version: its number as a heading, a rule under it, then the author's lines.
    ///
    /// A rule rather than only a bold heading, because several versions of terse one-line notes run
    /// together otherwise and the reader loses track of which line belongs to which release.
    /// </summary>
    private static Control CreateVersion(ModChangelogEntry entry)
    {
        var lines = new StackPanel { Spacing = 4, Margin = new Avalonia.Thickness(0, 8, 0, 0) };

        foreach (var line in entry.Lines)
        {
            lines.Children.Add(new SelectableTextBlock
            {
                Text = $"• {line}",
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children =
            {
                new SelectableTextBlock
                {
                    Text = string.Format(LocalizedTexts.Instance.GUIChangelogVersionHeading, entry.Version),
                    FontWeight = FontWeight.SemiBold,
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap
                },
                new Border
                {
                    Height = 1,
                    Background = Brushes.Gray,
                    Opacity = 0.4,
                    Margin = new Avalonia.Thickness(0, 4, 0, 0)
                },
                lines
            }
        };
    }

    private static string BuildPlainText(string modName, IReadOnlyList<ModChangelogEntry>? entries)
    {
        if (entries is null || entries.Count == 0) return modName;

        var blocks = entries.Select(entry =>
            string.Format(LocalizedTexts.Instance.GUIChangelogVersionHeading, entry.Version) +
            Environment.NewLine + entry.Text);

        return modName + Environment.NewLine + Environment.NewLine +
               string.Join(Environment.NewLine + Environment.NewLine, blocks);
    }
}
