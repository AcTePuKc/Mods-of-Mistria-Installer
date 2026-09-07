using Avalonia.Controls;
using Avalonia.Input;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaInstallerLib.Bindings;

namespace Garethp.ModsOfMistriaGUI.Views;

/// <summary>
/// Picks one key or controller button.
///
/// Keyboard triggers can be captured by pressing them, which is what anyone expects. Controller
/// buttons cannot: Avalonia has no gamepad input, so there is nothing to listen to. Rather than
/// pretend, the same list holds every name MMAPI accepts and the controller half is chosen from it.
///
/// The list is the whole vocabulary and nothing else. MMAPI documents that ALT, the lock keys and
/// the numpad are unsupported and that a mod configured with one silently falls back to its
/// default, so offering them would produce a binding that appears to save and then does nothing.
/// </summary>
public partial class BindingEditorWindow : Window
{
    private string? _result;
    private bool _cleared;
    private bool _capturing;

    public BindingEditorWindow() : this("", null)
    {
    }

    private BindingEditorWindow(string what, MmapiBinding? current)
    {
        InitializeComponent();

        var texts = LocalizedTexts.Instance;
        Title = texts.GUIBindingEditorTitle;
        WhatText.Text = what;
        TriggerLabel.Text = texts.GUIBindingEditorTrigger;
        NoteText.Text = texts.GUIBindingEditorNote;
        ClearButton.Content = texts.GUIBindingEditorClear;
        SaveButton.Content = texts.GUIBindingEditorSave;
        CancelButton.Content = texts.GUIClose;
        ToolTip.SetTip(ClearButton, texts.GUIBindingEditorClearTooltip);

        TriggerBox.ItemsSource = MmapiBindingVocabulary.AllNames;
        TriggerBox.SelectedItem = current?.Trigger ?? "F1";
        ShiftToggle.IsChecked = current?.Modifiers.Contains("SHIFT") == true;
        ControlToggle.IsChecked = current?.Modifiers.Contains("CONTROL") == true;

        TriggerBox.SelectionChanged += (_, _) => RefreshPreview();
        ShiftToggle.IsCheckedChanged += (_, _) => RefreshPreview();
        ControlToggle.IsCheckedChanged += (_, _) => RefreshPreview();

        CaptureButton.Click += (_, _) => StartCapture();
        ClearButton.Click += (_, _) =>
        {
            _cleared = true;
            _result = "";
            Close();
        };
        SaveButton.Click += (_, _) =>
        {
            _result = Preview();
            Close();
        };
        CancelButton.Click += (_, _) => Close();

        KeyDown += OnKeyDown;
        RefreshPreview();
        StopCapture();
    }

    /// <summary>
    /// The chosen binding name, an empty string when the user cleared it, or null when they
    /// cancelled. Empty and null are deliberately different: one is a decision, the other is not.
    /// </summary>
    public static async Task<string?> ShowAsync(Window owner, string what, MmapiBinding? current)
    {
        var window = new BindingEditorWindow(what, current);
        await window.ShowDialog(owner);
        return window._cleared ? "" : window._result;
    }

    // ── Capture ──────────────────────────────────────────────────────────────────

    private void StartCapture()
    {
        _capturing = true;
        CaptureButton.Content = LocalizedTexts.Instance.GUIBindingEditorCapturing;
        CaptureButton.Focus();
    }

    private void StopCapture()
    {
        _capturing = false;
        CaptureButton.Content = LocalizedTexts.Instance.GUIBindingEditorCapture;
    }

    private void OnKeyDown(object? sender, KeyEventArgs args)
    {
        // Escape means "stop listening" while capturing and "close" otherwise. It is not in
        // MMAPI's vocabulary, so it can never be the key the user is trying to bind.
        if (args.Key == Key.Escape)
        {
            args.Handled = true;
            if (_capturing) StopCapture();
            else Close();
            return;
        }

        if (!_capturing) return;

        var name = NameFor(args.Key);
        if (name is null) return;

        args.Handled = true;

        // A modifier pressed on its own is a legitimate binding - one mod ships SHIFT as its
        // reveal key - so it sets the trigger rather than only ticking the box.
        TriggerBox.SelectedItem = name;
        if (name is not "SHIFT" and not "CONTROL")
        {
            ShiftToggle.IsChecked = args.KeyModifiers.HasFlag(KeyModifiers.Shift);
            ControlToggle.IsChecked = args.KeyModifiers.HasFlag(KeyModifiers.Control);
        }

        StopCapture();
        RefreshPreview();
    }

    /// <summary>The MMAPI name for a pressed key, or null for a key MMAPI cannot bind.</summary>
    private static string? NameFor(Key key) => key switch
    {
        >= Key.F1 and <= Key.F12 => $"F{key - Key.F1 + 1}",
        >= Key.D0 and <= Key.D9 => $"{key - Key.D0}",
        >= Key.A and <= Key.Z => $"{(char)('A' + (key - Key.A))}",
        Key.Insert => "INSERT",
        Key.Delete => "DELETE",
        Key.Home => "HOME",
        Key.PageUp => "PAGE_UP",
        Key.PageDown => "PAGE_DOWN",
        Key.LeftShift or Key.RightShift => "SHIFT",
        Key.LeftCtrl or Key.RightCtrl => "CONTROL",
        _ => null
    };

    // ── Preview ──────────────────────────────────────────────────────────────────

    private string Preview()
    {
        var parts = new List<string>();
        if (ControlToggle.IsChecked == true) parts.Add("CONTROL");
        if (ShiftToggle.IsChecked == true) parts.Add("SHIFT");

        var trigger = TriggerBox.SelectedItem as string ?? "F1";
        parts.RemoveAll(part => part == trigger);
        parts.Add(trigger);

        return string.Join("+", parts);
    }

    private void RefreshPreview()
    {
        var preview = Preview();
        var binding = MmapiBindingVocabulary.TryParse(preview);

        PreviewText.Text = string.Format(LocalizedTexts.Instance.GUIBindingEditorPreview, preview);
        SaveButton.IsEnabled = binding is not null;
    }
}
