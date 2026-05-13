using Avalonia.Controls;
using Avalonia.Interactivity;
using MoergoLayerViz.App.ViewModels;

namespace MoergoLayerViz.App.Views;

/// <summary>
/// Modal dialog for choosing the auto-switch exit-tap key. <c>ShowDialog</c>
/// returns a <c>(bool ok, int? selection)</c> tuple — <c>ok=true</c> with
/// the chosen firmware key index (or <c>null</c> if the user explicitly
/// cleared the selection), <c>ok=false</c> when the user cancelled.
/// The window's <c>DataContext</c> must be an
/// <see cref="ExitKeyPickerViewModel"/>.
/// </summary>
public partial class ExitKeyPickerWindow : Window
{
    public ExitKeyPickerWindow()
    {
        InitializeComponent();
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is ExitKeyPickerViewModel vm)
            Close((true, vm.SelectedIndex));
        else
            Close((false, (int?)null));
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close((false, (int?)null));
}
