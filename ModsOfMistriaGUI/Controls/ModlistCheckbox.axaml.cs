using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.ViewModels;
using Garethp.ModsOfMistriaGUI.Views;

namespace Garethp.ModsOfMistriaGUI.Controls;

public partial class ModlistCheckbox : UserControl
{
    // Whether Shift was down when this row's checkbox was pressed.
    //
    // Captured on the press rather than read on the click because Avalonia's Click event carries no
    // modifier state - by then the keyboard is nobody's business. PointerPressed always precedes
    // the Click it produces, so this is set before it is read.
    private bool _extendPressed;

    public ModlistCheckbox()
    {
        InitializeComponent();
    }

    private void ModCheckboxPressed(object? sender, PointerPressedEventArgs e) =>
        _extendPressed = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

    /// <summary>
    /// Starts loading the newest release notes the first time the pointer reaches the icon.
    ///
    /// Loading every mod's notes when the list opens would be one Nexus call per mod on every
    /// launch, which no rate limit survives - so the fetch waits until someone actually looks. The
    /// tooltip is bound to the property being filled in, so it updates underneath an open tooltip.
    /// </summary>
    private void ChangelogHovered(object? sender, PointerEventArgs e)
    {
        if (DataContext is not ModModel model) return;
        model.Commands?.LoadChangelogPreview.Execute(model);
    }

    /// <summary>
    /// Runs after the clicked row has already toggled, which is what makes the range follow it:
    /// the rest of the range is set to whatever this row just became.
    /// </summary>
    private void ModCheckboxClick(object? sender, RoutedEventArgs e)
    {
        var extend = _extendPressed;
        _extendPressed = false;

        if (DataContext is not ModModel model) return;

        // The row's own DataContext is the mod, so the page view model is reached through the tree
        // rather than through a binding - the same reason the row's right-click commands are handed
        // in rather than looked up.
        var page = this.FindAncestorOfType<ModlistPageView>();
        if (page?.DataContext is ModlistPageViewModel viewModel)
            viewModel.ExtendSelectionTo(model, extend);
    }
}
