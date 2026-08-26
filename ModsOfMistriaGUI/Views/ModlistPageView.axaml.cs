using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Garethp.ModsOfMistriaInstallerLib.ModTypes;
using Garethp.ModsOfMistriaGUI.Models;
using Garethp.ModsOfMistriaGUI.Services;
using Garethp.ModsOfMistriaGUI.ViewModels;

namespace Garethp.ModsOfMistriaGUI.Views;

public partial class ModlistPageView : UserControl
{
    // A logical mod ID is shared by folder/ZIP/RAR copies. Drag data must
    // identify the physical source so moving one copy cannot move another.
    private const string ModDragDataFormat = "application/x-aim-mod-source";
    private const double DragAutoScrollEdge = 48;
    private Grid? _activeDropTarget;
    private int _dragAutoScrollDirection;
    private readonly DispatcherTimer _dragAutoScrollTimer;

    public ModlistPageView()
    {
        InitializeComponent();
        _dragAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _dragAutoScrollTimer.Tick += OnDragAutoScrollTick;
        AttachedToVisualTree += (_, _) => UpdateLanguageCheckmark();
    }

    // Route ComboBox SelectionChanged to SwitchProfileCommand.
    // The ComboBox binding is Mode=OneWay so the ViewModel's CurrentProfile is
    // NOT updated by user selection — we must explicitly call the command and let
    // it update CurrentProfile on success (or restore ComboBox on cancel).
    private async void OnProfileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb) return;
        if (cb.SelectedItem is not string newProfile) return;
        if (DataContext is not ModlistPageViewModel vm) return;
        if (newProfile == vm.CurrentProfile) return; // programmatic update, not user action

        if (vm.SwitchProfileCommand is IAsyncRelayCommand<string> asyncCmd)
            await asyncCmd.ExecuteAsync(newProfile);
        else
            vm.SwitchProfileCommand.Execute(newProfile);

        // If the switch was cancelled, reset the ComboBox back to the actual current profile
        if ((string?)cb.SelectedItem != vm.CurrentProfile)
            cb.SelectedItem = vm.CurrentProfile;
    }

    // The header checkbox is a visual summary (checked / unchecked / mixed), not
    // a two-way boolean setting. Route its click through the same commands as the
    // Cog menu so both entry points always change the complete selection alike.
    private void ToggleAllModsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ModlistPageViewModel vm || !vm.CanChangeModSelection)
            return;

        if (vm.AllModsSelected == true)
            vm.DisableAllModsCommand.Execute(null);
        else
            vm.EnableAllModsCommand.Execute(null);

        e.Handled = true;
    }

    private void OnModDragGripPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control grip || grip.DataContext is not ModModel mod) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (DataContext is not ModlistPageViewModel { CanDragReorderMods: true }) return;

        var data = new DataObject();
        data.Set(ModDragDataFormat, DuplicateModDetector.NormalizeSource(mod.Mod.GetSourcePath()));
        var row = grip.GetVisualAncestors()
            .OfType<Grid>()
            .FirstOrDefault(grid => grid.DataContext is ModModel);
        row?.Classes.Add("dragging");
        try
        {
            DragDrop.DoDragDrop(e, data, DragDropEffects.Move);
        }
        finally
        {
            StopDragAutoScroll();
            row?.Classes.Remove("dragging");
            if (_activeDropTarget is not null) ClearDropIndicator(_activeDropTarget);
        }
        e.Handled = true;
    }

    private void OnModRowDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Grid target) return;

        if (DataContext is not ModlistPageViewModel { CanDragReorderMods: true })
        {
            e.DragEffects = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.DragEffects = e.Data.Contains(ModDragDataFormat)
            ? DragDropEffects.Move
            : DragDropEffects.None;

        if (e.DragEffects == DragDropEffects.Move)
        {
            ShowDropIndicator(target, e.GetPosition(target).Y < target.Bounds.Height / 2);
            UpdateDragAutoScroll(e);
        }
        else
        {
            StopDragAutoScroll();
        }

        e.Handled = true;
    }

    private void OnModRowDragLeave(object? sender, RoutedEventArgs e)
    {
        StopDragAutoScroll();
        if (sender is Grid target && ReferenceEquals(target, _activeDropTarget))
            ClearDropIndicator(target);
    }

    private void OnModRowDrop(object? sender, DragEventArgs e)
    {
        StopDragAutoScroll();
        if (sender is not Grid target || target.DataContext is not ModModel targetMod) return;
        if (DataContext is not ModlistPageViewModel { CanDragReorderMods: true } vm) return;
        if (e.Data.Get(ModDragDataFormat) is not string draggedSource) return;

        var draggedMod = vm.Mods.FirstOrDefault(mod =>
            string.Equals(
                DuplicateModDetector.NormalizeSource(mod.Mod.GetSourcePath()),
                draggedSource,
                StringComparison.OrdinalIgnoreCase));
        if (draggedMod is null) return;

        var insertBeforeTarget = e.GetPosition(target).Y < target.Bounds.Height / 2;
        vm.MoveMod(draggedMod, targetMod, insertBeforeTarget);
        ClearDropIndicator(target);
        // ItemsRepeater may recycle the old target container during Move. Wait until
        // bindings have settled, then flash the row that now represents the dragged mod.
        Dispatcher.UIThread.Post(() =>
        {
            var movedRow = this.GetVisualDescendants()
                .OfType<Grid>()
                .FirstOrDefault(grid => grid.Classes.Contains("mod-row") &&
                                        ReferenceEquals(grid.DataContext, draggedMod));
            if (movedRow is not null) ShowDropCompleteFlash(movedRow);
        }, DispatcherPriority.Background);
        e.Handled = true;
    }

    private void UpdateDragAutoScroll(DragEventArgs e)
    {
        var position = e.GetPosition(ModListScrollViewer);
        var viewportHeight = ModListScrollViewer.Bounds.Height;

        if (viewportHeight <= 0 || position.Y < 0 || position.Y > viewportHeight)
        {
            StopDragAutoScroll();
            return;
        }

        var direction = position.Y <= DragAutoScrollEdge
            ? -1
            : position.Y >= viewportHeight - DragAutoScrollEdge
                ? 1
                : 0;

        if (direction == 0)
        {
            StopDragAutoScroll();
            return;
        }

        _dragAutoScrollDirection = direction;
        if (!_dragAutoScrollTimer.IsEnabled)
            _dragAutoScrollTimer.Start();
    }

    private void OnDragAutoScrollTick(object? sender, EventArgs e)
    {
        if (_dragAutoScrollDirection < 0)
            ModListScrollViewer.LineUp();
        else if (_dragAutoScrollDirection > 0)
            ModListScrollViewer.LineDown();
        else
            StopDragAutoScroll();
    }

    private void StopDragAutoScroll()
    {
        _dragAutoScrollDirection = 0;
        _dragAutoScrollTimer.Stop();
    }

    private void ShowDropIndicator(Grid target, bool before)
    {
        if (_activeDropTarget is not null && !ReferenceEquals(_activeDropTarget, target))
            ClearDropIndicator(_activeDropTarget);

        _activeDropTarget = target;
        FindDropIndicator(target, "DropBeforeIndicator")!.IsVisible = before;
        FindDropIndicator(target, "DropAfterIndicator")!.IsVisible = !before;
    }

    private void ClearDropIndicator(Grid target)
    {
        var before = FindDropIndicator(target, "DropBeforeIndicator");
        var after = FindDropIndicator(target, "DropAfterIndicator");
        if (before is not null) before.IsVisible = false;
        if (after is not null) after.IsVisible = false;
        if (ReferenceEquals(_activeDropTarget, target)) _activeDropTarget = null;
    }

    private static void ShowDropCompleteFlash(Grid target)
    {
        var flash = FindDropIndicator(target, "DropCompleteFlash");
        if (flash is null) return;

        flash.Opacity = 0.55;
        DispatcherTimer.RunOnce(() => flash.Opacity = 0, TimeSpan.FromMilliseconds(190));
    }

    private static Border? FindDropIndicator(Grid target, string name) =>
        target.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(border => border.Name == name);

    private void LanguageMenuClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string languageCode }) return;
        if (DataContext is not ModlistPageViewModel viewModel) return;

        viewModel.SetLanguageCommand.Execute(languageCode);
        UpdateLanguageCheckmark();
    }

    private void SettingsMenuClick(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is MainWindow window &&
            window.DataContext is MainWindowViewModel viewModel)
            viewModel.ShowSettings();
    }

    private void UpdateLanguageCheckmark()
    {
        var selected = LocalizationService.Instance.LanguageCode;
        var items = new[]
        {
            LanguageSystemMenuItem, LanguageEnglishMenuItem, LanguageBulgarianMenuItem,
            LanguagePolishMenuItem, LanguageGermanMenuItem, LanguageFrenchMenuItem,
            LanguageDutchMenuItem, LanguagePortugueseMenuItem, LanguageRussianMenuItem,
            LanguageIndonesianMenuItem, LanguageSimplifiedChineseMenuItem,
            LanguageTraditionalChineseMenuItem, LanguageKoreanMenuItem,
            LanguageJapaneseMenuItem, LanguageSpanishMenuItem, LanguageUkrainianMenuItem
        };

        foreach (var item in items)
        {
            var isSelected = string.Equals(item.Tag as string, selected, StringComparison.OrdinalIgnoreCase);
            item.Icon = isSelected
                ? new TextBlock
                {
                    Text = "✓",
                    FontWeight = FontWeight.Bold,
                    VerticalAlignment = VerticalAlignment.Center
                }
                : null;
        }
    }
}
