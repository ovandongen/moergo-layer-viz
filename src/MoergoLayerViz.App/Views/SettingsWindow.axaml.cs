using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using MoergoLayerViz.App.ViewModels;

namespace MoergoLayerViz.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        // Refresh the running-process list when the picker button is clicked,
        // just before the flyout opens. Button.Click fires synchronously
        // before Flyout.Open, so the bound ListBox shows fresh data.
        if (this.FindControl<Button>("PickProcessButton") is { } btn)
        {
            btn.Click += (_, _) =>
            {
                if (DataContext is SettingsViewModel vm)
                    vm.RefreshRunningProcesses();
            };
        }
    }

    // TextBlock has no built-in click command, so the update-link's
    // PointerPressed handler lives here in the code-behind.
    private void OnUpdateLinkClick(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.UpdateUrl is { } url)
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    // ListBox row selection → drop the process name into the new-rule textbox
    // and close the flyout. Using SelectionChanged (not a per-item
    // PointerPressed) makes the whole row hit-testable, not just the text glyphs.
    private void OnProcessPickerSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListBox lb || lb.SelectedItem is not string name) return;
        if (DataContext is not SettingsViewModel vm) return;
        vm.PickProcessCommand.Execute(name);
        lb.SelectedItem = null;
        if (this.FindControl<Button>("PickProcessButton") is { } btn &&
            btn.Flyout is FlyoutBase flyout)
        {
            flyout.Hide();
        }
    }

    /// <summary>
    /// Opens the exit-tap key picker as a modal dialog. On OK, hands the
    /// resulting (nullable) firmware key index back to the
    /// SettingsViewModel, which persists it via the main VM (which re-arms
    /// the <c>MultiTapDetector</c>). Cancel = no-op.
    /// </summary>
    private async void OnPickExitKeysClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        var profile = vm.ActiveKeyboardProfile;
        if (profile is null) return;

        var pickerVm = new ExitKeyPickerViewModel(profile, vm.ExitTapKey);
        var window = new ExitKeyPickerWindow { DataContext = pickerVm };
        var result = await window.ShowDialog<(bool ok, int? selection)>(this);
        if (!result.ok) return;
        vm.ApplyExitTapKey(result.selection);
    }
}
