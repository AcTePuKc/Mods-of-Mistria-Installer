using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Garethp.ModsOfMistriaGUI.Models;

namespace Garethp.ModsOfMistriaGUI.Controls;

public partial class ModlistCheckbox : UserControl
{
    private Popup? _descriptionPopup;

    public ModlistCheckbox()
    {
        InitializeComponent();
    }

    private void OnDescriptionInfoPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Control target || DataContext is not ModModel model || string.IsNullOrWhiteSpace(model.Description))
            return;

        CloseDescriptionPopup();
        var popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Right,
            HorizontalOffset = 6,
            IsLightDismissEnabled = false,
            Topmost = true,
            Child = new Border
            {
                IsHitTestVisible = false,
                MaxWidth = 520,
                Padding = new Thickness(10, 7),
                Background = new SolidColorBrush(Color.Parse("#2C2C2C")),
                BorderBrush = new SolidColorBrush(Color.Parse("#5A5A5A")),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Child = new TextBlock
                {
                    Text = model.Description,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        };
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(_descriptionPopup, popup)) _descriptionPopup = null;
        };
        _descriptionPopup = popup;
        popup.IsOpen = true;
    }

    private void OnDescriptionInfoPointerExited(object? sender, PointerEventArgs e)
    {
        CloseDescriptionPopup();
    }

    private void CloseDescriptionPopup()
    {
        if (_descriptionPopup is null) return;
        _descriptionPopup.IsOpen = false;
        _descriptionPopup = null;
    }

}
