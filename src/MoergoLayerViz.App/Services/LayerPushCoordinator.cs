using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the two layer-push engines (<see cref="AutoSwitchEngine"/>,
/// <see cref="MouseLayerEngine"/>) and the handoff between them. Pushes from
/// either engine route through this class to a single <see cref="IHidPipeline"/>,
/// last-write-wins, except that AutoSwitch pushes are redirected into the
/// MouseLayer engine's pending revert target while a mouse-layer push is in
/// flight — so the eventual revert lands on the app rule's layer in one
/// transition instead of ping-ponging.
///
/// <para>AutoSwitch receives "the layer underneath any in-flight mouse-layer
/// push" via its callback so its captured PreRuleLayer never observes the
/// transient mouse layer.</para>
/// </summary>
public sealed class LayerPushCoordinator : IDisposable
{
    private readonly IHidPipeline _hid;
    private bool _disposed;

    public AutoSwitchEngine AutoSwitch { get; }
    public MouseLayerEngine? MouseLayer { get; }

    /// <summary>Re-raised key-position events for the host's press-highlight tracker.</summary>
    public event Action<int, bool>? KeyPositionForUi;

    public LayerPushCoordinator(
        IHidPipeline hid,
        Func<int> getRenderedLayer,
        IKeyboardProfile profile,
        ISettingsService settings,
        IActiveWindowMonitor? activeWindowMonitor,
        IMouseIdleMonitor? mouseIdleMonitor)
    {
        _hid = hid;

        // AutoSwitch reads "the layer underneath any in-flight mouse-layer
        // push" so its captured PreRuleLayer (and userOverrode comparison)
        // never see the transient mouse layer. When no mouse push is active,
        // PreMoveLayer is null and we fall through to the rendered layer —
        // which is also the right answer for Cmd+Tab focus changes that
        // happen without mouse movement.
        AutoSwitch = new AutoSwitchEngine(
            settings,
            activeWindowMonitor,
            () => MouseLayer?.PreMoveLayer ?? getRenderedLayer(),
            profile);
        AutoSwitch.PushLayerRequested += OnAutoSwitchPushRequested;

        if (mouseIdleMonitor is not null)
        {
            MouseLayer = new MouseLayerEngine(
                settings,
                mouseIdleMonitor,
                getRenderedLayer,
                () => _hid.IsConnected,
                profile);
            MouseLayer.PushLayerRequested += _hid.PushLayer;
        }

        _hid.KeyPositionEvent += OnKeyPositionFromHid;
    }

    public void SetActiveProfile(IKeyboardProfile profile)
    {
        AutoSwitch.SetActiveProfile(profile);
        MouseLayer?.SetActiveProfile(profile);
    }

    /// <summary>Forwards a HID connection change so the mouse engine can start/stop the OS-level tap.</summary>
    public void OnHidConnectionChanged() => MouseLayer?.OnHidConnectionChanged();

    /// <summary>
    /// If a mouse-layer push is in flight, push the captured pre-move layer
    /// synchronously (bounded) so the keyboard isn't left pinned to the
    /// mouse layer when the host tears down HID.
    ///
    /// <para>Defensive fallback: if no push is in flight but the firmware
    /// is still on the configured mouse layer *and* the last layer change
    /// came from the app (not a user keypress), push base. This recovers
    /// the case where a fire-and-forget idle revert was lost to a HID
    /// error or disposal race — the engine thinks it reverted but the
    /// keyboard is still pinned. <see cref="IHidPipeline.IsLastChangeAppControlled"/>
    /// gates this so we don't override a layer the user explicitly
    /// selected from the keyboard.</para>
    /// </summary>
    public void RevertMouseLayerForShutdown(TimeSpan timeout)
    {
        if (MouseLayer is null) return;

        if (MouseLayer.PreMoveLayer is int layer)
        {
            _hid.PushLayerSync(layer, timeout);
            MouseLayer.ClearPushedState();
            return;
        }

        if (MouseLayer.CurrentSettings.MouseLayerIndex is int mouseIdx
            && _hid.CurrentLayer == mouseIdx
            && _hid.IsLastChangeAppControlled)
        {
            DiagnosticLog.Info("MouseLayer",
                $"shutdown: keyboard still on app-pushed mouse layer {mouseIdx}, reverting to base");
            _hid.PushLayerSync(0, timeout);
        }
    }

    public void Shutdown()
    {
        AutoSwitch.Shutdown();
        MouseLayer?.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _hid.KeyPositionEvent -= OnKeyPositionFromHid;
        AutoSwitch.PushLayerRequested -= OnAutoSwitchPushRequested;
        if (MouseLayer is not null)
            MouseLayer.PushLayerRequested -= _hid.PushLayer;
    }

    private void OnAutoSwitchPushRequested(int layer)
    {
        // AutoSwitch ↔ MouseLayer coordination: while the mouse layer is
        // actively pushing, redirect the app rule's target into the mouse
        // engine's revert layer so the keyboard stays on the mouse layer
        // until idle, then lands on the app rule's layer in one transition.
        if (MouseLayer?.TryRedirectPendingPush(layer) == true) return;
        _hid.PushLayer(layer);
    }

    private void OnKeyPositionFromHid(int position, bool pressed)
    {
        // Feed the exit-tap detector regardless of pressed/released — it
        // needs releases to re-arm. Null position short-circuits inside the
        // detector, so this is free when no exit key is configured.
        AutoSwitch.OnKeyPositionEvent(position, pressed);
        KeyPositionForUi?.Invoke(position, pressed);
    }
}
