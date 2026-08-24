using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Garethp.ModsOfMistriaGUI.Services;

namespace Garethp.ModsOfMistriaGUI.Views;

public partial class NexusAssociationWindow : Window
{
    public NexusAssociationWindow()
    {
        InitializeComponent();
        Title = Text("GUINexusAssociateTitle");
        HeadingText.Text = Text("GUINexusAssociateTitle");
        ExplanationText.Text = Text("GUINexusAssociateExplanation");
        LinkBox.Watermark = Text("GUINexusAssociateWatermark");
        CancelButton.Content = Text("GUICancel");
        SaveButton.Content = Text("GUISave");
    }

    public static Task<string?> ShowAsync(Window owner) =>
        new NexusAssociationWindow().ShowDialog<string?>(owner);

    private static string Text(string key) => LocalizationService.Instance[key];

    private async void OnLinkKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await SaveAsync();
    }

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private async Task SaveAsync()
    {
        var value = LinkBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(value))
        {
            Close(value);
            return;
        }

        StatusText.Text = Text("GUINexusAssociateMissing");
        await Task.CompletedTask;
    }
}
