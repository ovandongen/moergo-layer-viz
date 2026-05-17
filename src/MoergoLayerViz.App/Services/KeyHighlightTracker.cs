using MoergoLayerViz.App.ViewModels;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Pulses a key VM in response to a HID position event. Implementations may
/// freely choose how the pulse is timed and rendered; consumers only call
/// <see cref="PulseAt"/>.
/// </summary>
public interface IKeyHighlightTracker
{
    /// <summary>HID-thread entry — pulses the key at <paramref name="position"/>.
    /// Implementations are responsible for marshaling onto the UI thread.</summary>
    void PulseAt(int position);
}

/// <summary>
/// Owns the per-key pulse animation triggered by HID position events.
/// <see cref="PulseAt"/> is called from the HID thread; it posts to the UI
/// thread internally.
/// </summary>
public sealed class KeyHighlightTracker : IKeyHighlightTracker
{
    private readonly IReadOnlyList<KeyViewModel> _keys;
    private readonly Dictionary<KeyViewModel, CancellationTokenSource> _pressCts = new();
    private const int PressHighlightMs = 90;

    public KeyHighlightTracker(IReadOnlyList<KeyViewModel> keys)
    {
        _keys = keys;
    }

    /// <summary>HID-thread entry — pulses the key at <paramref name="position"/>.
    /// Posts to UI thread internally.</summary>
    public void PulseAt(int position)
    {
        if (position < 0 || position >= _keys.Count) return;
        var vm = _keys[position];
        Avalonia.Threading.Dispatcher.UIThread.Post(() => PulseKeyPress(vm));
    }

    private void PulseKeyPress(KeyViewModel vm)
    {
        if (_pressCts.TryGetValue(vm, out var existing))
        {
            existing.Cancel();
            existing.Dispose();
        }
        var cts = new CancellationTokenSource();
        _pressCts[vm] = cts;
        vm.IsPressed = true;

        _ = Task.Delay(PressHighlightMs, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_pressCts.TryGetValue(vm, out var stored) && stored == cts)
                {
                    vm.IsPressed = false;
                    _pressCts.Remove(vm);
                    cts.Dispose();
                }
            });
        }, TaskScheduler.Default);
    }
}
