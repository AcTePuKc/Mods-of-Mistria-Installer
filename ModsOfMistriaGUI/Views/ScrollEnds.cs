using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Garethp.ModsOfMistriaGUI.Services;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// Puts a jump-to-top and a jump-to-bottom button on a scrollable area.
///
/// The issue report is the worst offender: a single conflict between two cosmetics mods can list
/// seventy-four shared files, and the buttons that act on the issue are underneath all of them. The
/// scrollbar thumb in that situation is a few pixels tall and dragging it is a game of chance, so
/// the way back to the top was the mouse wheel, over and over.
///
/// The buttons are pinned to the corners of the viewport rather than placed in the flow, so they
/// stay reachable wherever the content has got to, and each hides itself when there is nothing in
/// its direction - an area that fits on screen shows neither, which is most of them.
/// </summary>
public static class ScrollEnds
{
    /// <summary>How far from an end still counts as being at it, in pixels.</summary>
    private const double Slack = 4;

    /// <summary>
    /// Adds the buttons beside a viewer that already sits in a <see cref="Panel"/> - which is the
    /// case for every scroll area declared in XAML here. The viewer's layout position is copied so
    /// the buttons land in the same grid cell and overlay it.
    ///
    /// Both current hosts put the viewer in a star-sized row, where the buttons cannot affect the
    /// cell's height. Attaching to one in an auto-sized row would let a button's own height feed
    /// back into the viewport that decides whether the button is shown at all, so give that case a
    /// fixed height before reaching for this.
    /// </summary>
    public static void Attach(ScrollViewer viewer)
    {
        if (viewer.Parent is not Panel host) return;

        var (top, bottom) = Buttons(viewer);

        foreach (var button in new[] { top, bottom })
        {
            button.SetValue(Grid.RowProperty, viewer.GetValue(Grid.RowProperty));
            button.SetValue(Grid.ColumnProperty, viewer.GetValue(Grid.ColumnProperty));
            button.SetValue(Grid.RowSpanProperty, viewer.GetValue(Grid.RowSpanProperty));
            button.SetValue(Grid.ColumnSpanProperty, viewer.GetValue(Grid.ColumnSpanProperty));
            button.Margin = viewer.Margin + button.Margin;
            host.Children.Add(button);
        }
    }

    /// <summary>
    /// Wraps a viewer that is not in a panel yet - one being built in code and about to be handed
    /// to something as its content - and returns what to use in its place.
    /// </summary>
    public static Control Wrap(ScrollViewer viewer)
    {
        var (top, bottom) = Buttons(viewer);

        return new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Children = { viewer, top, bottom }
        };
    }

    private static (Button Top, Button Bottom) Buttons(ScrollViewer viewer)
    {
        var texts = LocalizedTexts.Instance;

        var top = Corner("▲", texts.GUIScrollToTop, VerticalAlignment.Top);
        var bottom = Corner("▼", texts.GUIScrollToBottom, VerticalAlignment.Bottom);

        top.Click += (_, _) => viewer.ScrollToHome();
        bottom.Click += (_, _) => viewer.ScrollToEnd();

        void Sync()
        {
            var scrollable = viewer.Extent.Height - viewer.Viewport.Height;

            top.IsVisible = scrollable > Slack && viewer.Offset.Y > Slack;
            bottom.IsVisible = scrollable > Slack && viewer.Offset.Y < scrollable - Slack;
        }

        viewer.ScrollChanged += (_, _) => Sync();

        // The first Sync has to wait for a layout pass: before one, Extent and Viewport are both
        // zero and every area would decide it has nothing to scroll to.
        viewer.LayoutUpdated += (_, _) => Sync();

        Sync();
        return (top, bottom);
    }

    private static Button Corner(string glyph, string tip, VerticalAlignment where)
    {
        var button = new Button
        {
            Content = new TextBlock { Text = glyph, FontSize = 11 },
            Padding = new Thickness(7, 2, 7, 2),
            MinWidth = 0,
            Opacity = 0.55,
            IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = where,
            // Clear of the scrollbar, which is what the user would otherwise be aiming at. The
            // vertical inset matters as much as the horizontal one: an expanded Fluent scrollbar
            // grows line-up and line-down arrows at its ends, roughly sixteen pixels tall, and a
            // four pixel inset parked this button on top of the one nearest it.
            Margin = where == VerticalAlignment.Top
                ? new Thickness(0, 22, 20, 0)
                : new Thickness(0, 0, 20, 22),
            CornerRadius = new CornerRadius(4)
        };

        ToolTip.SetTip(button, tip);

        // Faint until wanted: these sit on top of the content, and a pair of solid buttons parked
        // over somebody's file list is worse than the scrolling they save.
        button.PointerEntered += (_, _) => button.Opacity = 1;
        button.PointerExited += (_, _) => button.Opacity = 0.55;

        return button;
    }
}
