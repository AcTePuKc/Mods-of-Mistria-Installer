using Avalonia;
using Avalonia.Controls;
using Garethp.ModsOfMistriaInstallerLib;

namespace Garethp.ModsOfMistriaGUI.Services;

/// <summary>
/// Keeps a dialog inside the screen it opens on.
///
/// A window with a fixed height and <c>WindowStartupLocation="CenterOwner"</c> has a failure mode
/// that only shows up on somebody else's monitor: centring assumes the window fits. When it does
/// not - a laptop screen, or far more often display scaling, where 1080p at 150% leaves about 693
/// device-independent pixels of working area - centring a 700-tall dialog puts its top edge above
/// the top of the screen. The title bar and the first controls are then simply unreachable, and
/// because the window is still perfectly usable once maximised, it reads as a scaling quirk rather
/// than a bug worth reporting.
///
/// Avalonia does not clamp this for us, so every dialog does it on open: shrink to what the working
/// area can hold, then nudge the frame back inside it. The size is in device-independent pixels and
/// the position is in physical ones, which is the whole reason the scaling factor has to appear
/// here explicitly.
/// </summary>
public static class DialogBounds
{
    /// <summary>
    /// Shrinks the window to fit the working area of the screen it is on, then moves it fully on
    /// screen. Safe to call on a window that already fits, where it does nothing.
    /// </summary>
    /// <param name="fraction">
    /// How much of the working area a dialog may occupy. Slightly under one, so a clamped window
    /// still looks like a window rather than filling the screen edge to edge.
    /// </param>
    public static void FitToScreen(this Window window, double fraction = 0.94)
    {
        try
        {
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            if (screen is null) return;

            var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
            var work = screen.WorkingArea;

            var maxWidth = work.Width / scaling * fraction;
            var maxHeight = work.Height / scaling * fraction;

            // MinWidth and MinHeight are floors the window manager will enforce anyway, so a dialog
            // whose minimum is larger than the screen stays too big. Clamping the position below
            // still helps in that case: it puts the top-left corner on screen, which leaves the
            // title bar reachable so the user can resize or move it themselves.
            if (!double.IsNaN(window.Width) && window.Width > maxWidth)
                window.Width = Math.Max(window.MinWidth, maxWidth);

            if (!double.IsNaN(window.Height) && window.Height > maxHeight)
                window.Height = Math.Max(window.MinHeight, maxHeight);

            MoveOnScreen(window, work, scaling);
        }
        catch (Exception exception)
        {
            // Screen enumeration is a platform call and this is cosmetic. A dialog that opens in a
            // slightly wrong place is a far better outcome than one that throws while opening.
            Logger.Log($"Could not fit {window.GetType().Name} to the screen: {exception.Message}");
        }
    }

    private static void MoveOnScreen(Window window, PixelRect work, double scaling)
    {
        // FrameSize includes the title bar and borders, which is what actually has to fit. It is
        // null before the platform window exists and can be stale immediately after a resize, so
        // the client size is the fallback and the difference is small enough not to matter.
        var width = (int)Math.Ceiling((window.FrameSize?.Width ?? window.Width) * scaling);
        var height = (int)Math.Ceiling((window.FrameSize?.Height ?? window.Height) * scaling);

        // Math.Max guards the case where the window is still wider or taller than the working area:
        // the right-hand bound would then be less than the left-hand one and Clamp would throw.
        var x = Math.Clamp(window.Position.X, work.X, Math.Max(work.X, work.Right - width));
        var y = Math.Clamp(window.Position.Y, work.Y, Math.Max(work.Y, work.Bottom - height));

        window.Position = new PixelPoint(x, y);
    }
}
