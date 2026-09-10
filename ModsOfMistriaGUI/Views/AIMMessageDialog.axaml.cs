using Avalonia.Controls;
using Avalonia.Interactivity;
using Garethp.ModsOfMistriaGUI.Services;
using MsBox.Avalonia.Enums;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// The common text dialog for AIM. Unlike the third-party message box, long localized messages
/// stay inside a bounded, scrollable area and the buttons remain reachable at every DPI.
/// </summary>
public partial class AIMMessageDialog : Window
{
    private readonly TaskCompletionSource<ButtonResult> _result =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Required by Avalonia's compiled XAML loader; callers use GetMessageBoxStandard.
    public AIMMessageDialog() : this("", "", ButtonEnum.Ok)
    {
    }

    private AIMMessageDialog(string title, string message, ButtonEnum buttons)
    {
        InitializeComponent();
        App.ApplyThemeClass(this);
        Title = title;
        MessageText.Text = message;
        Closed += (_, _) => _result.TrySetResult(ButtonResult.Cancel);

        switch (buttons)
        {
            case ButtonEnum.Ok:
                AddButton("GUIOk", ButtonResult.Ok, isDefault: true, isCancel: true);
                break;
            case ButtonEnum.YesNo:
                AddButton("GUIYes", ButtonResult.Yes, isDefault: true);
                AddButton("GUINo", ButtonResult.No, isCancel: true);
                break;
            case ButtonEnum.YesNoCancel:
                AddButton("GUIYes", ButtonResult.Yes, isDefault: true);
                AddButton("GUINo", ButtonResult.No);
                AddButton("GUICancel", ButtonResult.Cancel, isCancel: true);
                break;
            default:
                AddButton("GUIOk", ButtonResult.Ok, isDefault: true, isCancel: true);
                break;
        }

        Opened += (_, _) => this.FitToScreen(0.86);
    }

    public static AIMMessageDialog GetMessageBoxStandard(
        string title,
        string message,
        ButtonEnum buttons = ButtonEnum.Ok) => new(title, message, buttons);

    public async Task<ButtonResult> ShowAsync()
    {
        var owner = App.TopLevel as Window;
        if (owner is null)
        {
            Show();
        }
        else
        {
            _ = ShowDialog(owner);
        }

        return await _result.Task;
    }

    private void AddButton(
        string resourceKey,
        ButtonResult result,
        bool isDefault = false,
        bool isCancel = false)
    {
        var button = new Button
        {
            Content = LocalizationService.Instance[resourceKey],
            MinWidth = 84,
            IsDefault = isDefault,
            IsCancel = isCancel
        };
        button.Click += (_, _) =>
        {
            _result.TrySetResult(result);
            Close(result);
        };
        ButtonPanel.Children.Add(button);
    }
}
