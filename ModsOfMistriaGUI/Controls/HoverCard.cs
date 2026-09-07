using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Garethp.ModsOfMistriaGUI.Controls;

/// <summary>
/// What a badge on a mod row says when you hover it - shown inside the window rather than as a
/// tooltip.
///
/// A ToolTip is a separate top-level window. On Windows, opening one while a control in the main
/// window holds keyboard focus - clicking the mod search box is all it takes - costs the main
/// window its activation, and Avalonia reports that to the row underneath as the pointer leaving
/// it. The tooltip closes; the pointer is still on the badge, so it opens again; and the badge
/// flickers for as long as you look at it. Moving the mod description out of a tooltip and behind
/// an icon did not fix it, because the replacement was still a popup window.
///
/// This is not a window. The card is a child of the window's own overlay layer, so there is nothing
/// to activate, nothing to take focus away from the search box, and no loop to get into. It is not
/// hit-testable either, so it cannot pull the pointer off the badge that opened it.
///
/// The text comes from the badge's <see cref="Control.Tag"/>, and is bound rather than copied, so
/// an explanation that arrives later - the changelog preview is fetched on hover - fills the card
/// in while it is open.
/// </summary>
public static class HoverCard
{
    // Only one badge can be under the pointer at a time, whichever row it belongs to.
    private static Border? _card;
    private static OverlayLayer? _layer;

    // The binding's source is the badge, which outlives the card by a long way, so dropping the
    // card without unsubscribing would leave one dead card rooted per hover.
    private static IDisposable? _binding;

    // The badge the open card belongs to, so a row recycled out from under the pointer - the search
    // filter changing what is on screen, with no scroll to notice - takes its card with it rather
    // than leaving it hanging over whatever row moved into that spot.
    private static Control? _target;
    private static EventHandler<VisualTreeAttachmentEventArgs>? _targetDetached;

    public static void Show(object? sender)
    {
        // First, unconditionally: whatever happens next, the card that was open belongs to a badge
        // the pointer has left.
        Hide();

        if (sender is not Control target) return;

        var layer = OverlayLayer.GetOverlayLayer(target);
        if (layer is null) return;

        var text = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 520,
            Foreground = Brushes.White
        };
        var binding = text.Bind(TextBlock.TextProperty,
            new Binding(nameof(Control.Tag)) { Source = target, Mode = BindingMode.OneWay });

        var card = new Border
        {
            IsHitTestVisible = false,

            // Hidden until there is something to say, so an explanation that is still loading shows
            // nothing rather than an empty box that then jumps to full size.
            IsVisible = !string.IsNullOrWhiteSpace(text.Text),
            Padding = new Thickness(10, 7),
            Background = new SolidColorBrush(Color.Parse("#2C2C2C")),
            BorderBrush = new SolidColorBrush(Color.Parse("#5A5A5A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = text
        };

        layer.Children.Add(card);
        _card = card;
        _layer = layer;
        _binding = binding;
        _target = target;
        _targetDetached = (_, _) => Hide();
        target.DetachedFromVisualTree += _targetDetached;

        Position(card, target, layer);

        text.PropertyChanged += (_, args) =>
        {
            if (args.Property != TextBlock.TextProperty || !ReferenceEquals(_card, card)) return;

            card.IsVisible = !string.IsNullOrWhiteSpace(text.Text);
            Position(card, target, layer);
        };
    }

    public static void Hide()
    {
        if (_card is not null) _layer?.Children.Remove(_card);
        if (_target is not null && _targetDetached is not null)
            _target.DetachedFromVisualTree -= _targetDetached;
        _binding?.Dispose();

        _card = null;
        _layer = null;
        _binding = null;
        _target = null;
        _targetDetached = null;
    }

    private static void Position(Border card, Visual target, Visual layer)
    {
        card.Measure(new Size(layer.Bounds.Width, layer.Bounds.Height));
        var size = card.DesiredSize;

        // Below the badge by default, above it when there is no room below, and never past either
        // edge - a card that opens off the side of a narrow window is no better than no card.
        var anchor = target.TranslatePoint(new Point(0, target.Bounds.Height + 4), layer) ?? default;

        var left = Math.Clamp(anchor.X, 0, Math.Max(0, layer.Bounds.Width - size.Width));
        var top = anchor.Y + size.Height > layer.Bounds.Height
            ? Math.Max(0, anchor.Y - target.Bounds.Height - size.Height - 8)
            : anchor.Y;

        Canvas.SetLeft(card, left);
        Canvas.SetTop(card, top);
    }
}
