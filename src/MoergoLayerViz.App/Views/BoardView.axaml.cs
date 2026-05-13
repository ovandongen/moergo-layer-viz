using Avalonia.Controls;
using Avalonia.Input;
using MoergoLayerViz.App.ViewModels;

namespace MoergoLayerViz.App.Views;

public partial class BoardView : UserControl
{
    public BoardView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Routes a cap tap to the hosting <see cref="IBoardSurface"/>'s
    /// <c>OnKeyTapped</c> handler. Main window leaves the handler null so
    /// taps are inert; the exit-key picker provides a real handler that
    /// adds/removes the cap's index from the in-progress selection.
    /// </summary>
    private void OnKeyPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control c) return;
        if (c.DataContext is not KeyViewModel keyVm) return;
        if (DataContext is not IBoardSurface surface) return;
        var handler = surface.OnKeyTapped;
        if (handler is null) return;
        handler(keyVm.Position.Index);
        e.Handled = true;
    }
}
