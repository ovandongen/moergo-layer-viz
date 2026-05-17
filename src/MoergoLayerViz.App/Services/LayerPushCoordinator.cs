using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Models;
using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the two layer-push engines (<see cref="AutoSwitchEngine"/>,
/// <see cref="MouseLayerEngine"/>) and the handoff between them. See
/// <see cref="LayerPushCoordinator"/> for the full contract; this interface
/// captures the public surface for consumers and tests.
/// </summary>
public interface ILayerPushCoordinator : IDisposable
{
    AutoSwitchEngine AutoSwitch { get; }
    IMouseLayerEngine? MouseLayer { get; }

    /// <summary>Re-raised key-position events for the host's press-highlight tracker.</summary>
    event Action<int, bool>? KeyPositionForUi;

    void SetTransparencyPredicate(Func<int, int, TransparentBindingKind?>? classifier);
    void SetActiveProfile(IKeyboardProfile profile);
    void OnHidConnectionChanged();
    void RevertMouseLayerForShutdown(TimeSpan timeout);
    void Shutdown();
}

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
public sealed class LayerPushCoordinator : ILayerPushCoordinator
{
    private readonly IHidPipeline _hid;
    private Func<int, int, TransparentBindingKind?> _classifyBinding;
    private bool _disposed;

    public AutoSwitchEngine AutoSwitch { get; }
    public IMouseLayerEngine? MouseLayer { get; }

    /// <summary>Re-raised key-position events for the host's press-highlight tracker.</summary>
    public event Action<int, bool>? KeyPositionForUi;

    /// <summary>
    /// Replaces the binding classifier the coordinator uses to detect the
    /// transparent-key / empty-key exit gestures. Returns
    /// <see cref="TransparentBindingKind.Transparent"/> for <c>&amp;trans</c>,
    /// <see cref="TransparentBindingKind.Empty"/> for <c>&amp;none</c>, and
    /// <c>null</c> for any other binding. Called by the host when a new
    /// keyboard config is loaded (or cleared). Default classifier always
    /// returns null — the feature is inert until a config is bound.
    /// </summary>
    public void SetTransparencyPredicate(Func<int, int, TransparentBindingKind?>? classifier)
        => _classifyBinding = classifier ?? ((_, _) => null);

    public LayerPushCoordinator(
        IHidPipeline hid,
        Func<int> getRenderedLayer,
        IKeyboardProfile profile,
        ISettingsService settings,
        IActiveWindowMonitor? activeWindowMonitor,
        IMouseIdleMonitor? mouseIdleMonitor)
    {
        _hid = hid;
        _classifyBinding = (_, _) => null;

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

        // Transparent / empty-key exit: only on press (release would
        // double-fire). &trans and &none are independent per-engine opt-ins,
        // so the host can offer "exit on either / one / neither". Mouse-layer
        // push takes precedence — it's the visually-on-top push and its
        // revert lands on the captured pre-move layer, which is what the
        // user expects to return to.
        if (!pressed) return;
        if (_classifyBinding(_hid.CurrentLayer, position) is not TransparentBindingKind kind) return;
        bool isTrans = kind == TransparentBindingKind.Transparent;
        string reason = isTrans ? "transparent key tap" : "empty key tap";

        if (MouseLayer is { PreMoveLayer: not null, CurrentSettings: var ms }
            && ((isTrans && ms.ExitOnTransparentKey) || (!isTrans && ms.ExitOnEmptyKey)))
        {
            MouseLayer.RevertNow(reason);
            return;
        }

        if ((isTrans && AutoSwitch.ExitOnTransparentKey)
            || (!isTrans && AutoSwitch.ExitOnEmptyKey))
            AutoSwitch.ExitToFallback(reason);
    }
}
