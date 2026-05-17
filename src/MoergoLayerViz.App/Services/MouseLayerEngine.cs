using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the mouse-movement → keyboard layer push state machine. See
/// <see cref="MouseLayerEngine"/> for the full contract; this interface
/// captures the public surface so consumers can substitute an alternate
/// impl (tests, mocks).
/// </summary>
public interface IMouseLayerEngine : IDisposable
{
    /// <summary>Raised when the engine wants a layer pushed via HID.</summary>
    event Action<int>? PushLayerRequested;

    /// <summary>Current per-profile settings as last applied / loaded.</summary>
    MouseLayerSettings CurrentSettings { get; }

    /// <summary>The layer captured at move-start, or null if no push is in flight.</summary>
    int? PreMoveLayer { get; }

    void ApplySettings(MouseLayerSettings settings);
    void SetActiveProfile(IKeyboardProfile profile);
    void OnHidConnectionChanged();

    /// <summary>Marks the in-flight push as consumed without firing another revert.</summary>
    void ClearPushedState();

    /// <summary>External revert trigger; fires the captured pre-move layer if a push is in flight.</summary>
    void RevertNow(string reason);

    /// <summary>Redirect another engine's pending push into this engine's revert target.</summary>
    bool TryRedirectPendingPush(int layer);
}

/// <summary>
/// Owns the mouse-movement → keyboard layer push state machine. Subscribes
/// to an <see cref="IMouseIdleMonitor"/>: on movement, captures the current
/// (HID-reported) layer and pushes the configured "mouse layer" to the
/// keyboard; on idle, pushes back the captured pre-move layer.
///
/// <para>The revert target is intentionally not user-configurable: the
/// mouse layer must never itself become a "previous" / fallback target,
/// otherwise downstream engines (AutoSwitch in particular) would capture a
/// stale mouse-layer snapshot and revert to it later. If a user wants
/// "always go back to base", they configure the AutoSwitch fallback to
/// Base — the mouse engine just unwinds its own push.</para>
///
/// <para>Inert when disabled, no mouse layer is configured, or HID is not
/// connected — the engine still listens but every push request is suppressed
/// upstream (<see cref="PushLayerRequested"/> only fires when a valid push
/// can be made).</para>
///
/// <para><b>Interaction with <see cref="AutoSwitchEngine"/>:</b> both engines
/// push via the same HID command sink (<c>MainWindowViewModel.PushLayerToKeyboard</c>),
/// last-write-wins. AutoSwitch reads the engine's <see cref="PreMoveLayer"/>
/// when capturing its own PreRuleLayer so a mid-mouse-push focus change
/// doesn't snapshot the mouse layer as the app rule's "previous".</para>
/// </summary>
public sealed class MouseLayerEngine : IMouseLayerEngine
{
    private readonly ISettingsService _settingsService;
    private readonly IMouseIdleMonitor _monitor;
    private readonly Func<int> _getActiveLayer;
    private readonly Func<bool> _isHidConnected;

    private IKeyboardProfile _profile;
    private MouseLayerSettings _settings;
    private int? _preMoveLayer;
    private bool _disposed;

    /// <summary>Raised when the engine wants a layer pushed to the keyboard
    /// via HID. The host applies it via the firmware command channel
    /// (<c>MainWindowViewModel.PushLayerToKeyboard</c>).</summary>
    public event Action<int>? PushLayerRequested;

    public MouseLayerEngine(
        ISettingsService settingsService,
        IMouseIdleMonitor monitor,
        Func<int> getActiveLayer,
        Func<bool> isHidConnected,
        IKeyboardProfile initialProfile)
    {
        _settingsService = settingsService;
        _monitor = monitor;
        _getActiveLayer = getActiveLayer;
        _isHidConnected = isHidConnected;
        _profile = initialProfile;
        _settings = LoadSettingsForProfile(initialProfile.Id);

        _monitor.MoveStarted += OnMoveStarted;
        _monitor.MoveStopped += OnMoveStopped;
        _monitor.IdleTimeoutMs = _settings.IdleTimeoutMs;
        if (IsActive) _monitor.Start();
    }

    /// <summary>Current per-profile settings as last applied / loaded.</summary>
    public MouseLayerSettings CurrentSettings => _settings;

    /// <summary>
    /// True when the engine has all the prerequisites to push: enabled,
    /// a configured mouse layer, and HID connected. When this transitions
    /// false the monitor is stopped so the OS-level tap is released.
    /// </summary>
    private bool IsActive =>
        _settings.Enabled && _settings.MouseLayerIndex is not null && _isHidConnected();

    /// <summary>
    /// Replaces this profile's settings, persists them, re-configures the
    /// monitor (idle timeout + start/stop), and resets state so the next
    /// movement event captures a fresh pre-move layer. If the engine was
    /// currently pushing the mouse layer, the user's revert target is pushed
    /// first — otherwise disabling the feature mid-movement would leave the
    /// keyboard pinned to the mouse layer.
    /// </summary>
    public void ApplySettings(MouseLayerSettings settings)
    {
        RevertIfPushed("settings change");
        _settings = settings;
        _monitor.IdleTimeoutMs = settings.IdleTimeoutMs;
        var profileId = _profile.Id;
        PersistSetting(s =>
        {
            var clone = new Dictionary<string, MouseLayerSettings>(s.MouseLayer);
            clone[profileId] = settings;
            return s with { MouseLayer = clone };
        });
        // Stop+Start cycle (not just Start) so the monitor's internal state
        // machine resets — Start() is a no-op when already running, which
        // would otherwise leave _moving / _lastMoveTicks stale across the
        // settings change and the engine wouldn't fire MoveStarted on the
        // next real movement until a full natural idle→move cycle elapsed.
        _monitor.Stop();
        ReevaluateMonitorState();
    }

    /// <summary>Switches active keyboard profile. Reloads per-profile settings
    /// and reconfigures the monitor accordingly. Reverts any in-flight push
    /// on the outgoing profile first so we don't leave it pinned.</summary>
    public void SetActiveProfile(IKeyboardProfile profile)
    {
        RevertIfPushed("profile switch");
        _profile = profile;
        _settings = LoadSettingsForProfile(profile.Id);
        _monitor.IdleTimeoutMs = _settings.IdleTimeoutMs;
        ReevaluateMonitorState();
    }

    /// <summary>Notifies the engine that the HID-connected state changed so
    /// it can start/stop the monitor.</summary>
    public void OnHidConnectionChanged() => ReevaluateMonitorState();

    /// <summary>
    /// The layer captured at move-start (the layer the engine pushed the
    /// mouse layer over), or null if no mouse-layer push is currently in
    /// flight. Surfaced so AutoSwitch can read the "true underlying" layer
    /// instead of the live <c>ActiveLayerIndex</c> (which would be the
    /// transient mouse layer), and so the host can issue a synchronous
    /// revert push at shutdown.
    /// </summary>
    public int? PreMoveLayer => _preMoveLayer;

    /// <summary>Marks the in-flight push as consumed (without firing another
    /// revert). Called by the host after it issues a synchronous shutdown push.</summary>
    public void ClearPushedState() => _preMoveLayer = null;

    /// <summary>
    /// External revert trigger — fires the captured pre-move layer if a push
    /// is in flight, otherwise no-op. Used by the coordinator to unwind a
    /// push on a transparent / empty keypress (and the natural exit gesture
    /// when <see cref="MouseLayerSettings.EndlessTimeout"/> suppresses the
    /// idle-driven revert).
    /// </summary>
    public void RevertNow(string reason) => RevertIfPushed(reason);

    /// <summary>
    /// Called by the host when another engine (e.g. AutoSwitch) wants to push
    /// a layer while this engine is actively pushing the mouse layer. Rather
    /// than ping-pong the keyboard, the mouse layer stays held until movement
    /// stops and the captured pre-move target is swapped to <paramref name="layer"/>
    /// — so the eventual revert lands on the new layer. The host suppresses
    /// the other engine's push when this returns true.
    /// </summary>
    /// <returns><c>true</c> if a push is in flight and the redirect was
    /// recorded; <c>false</c> if no push is active and the caller should issue
    /// the push normally.</returns>
    public bool TryRedirectPendingPush(int layer)
    {
        if (_preMoveLayer is null) return false;
        DiagnosticLog.Info("MouseLayer",
            $"redirect: pre-move {_preMoveLayer} → {layer} (mouse keeps pushing until idle)");
        _preMoveLayer = layer;
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _monitor.MoveStarted -= OnMoveStarted;
        _monitor.MoveStopped -= OnMoveStopped;
        _monitor.Stop();
    }

    /// <summary>
    /// If a mouse-layer push is currently active, fire the captured
    /// pre-move layer and reset state. Used when the user disables /
    /// changes settings or switches profile mid-push — keeps the keyboard
    /// from being left pinned to the mouse layer.
    /// </summary>
    private void RevertIfPushed(string reason)
    {
        if (!_preMoveLayer.HasValue) return;
        var target = _preMoveLayer.Value;
        DiagnosticLog.Info("MouseLayer", $"{reason} → revert to layer {target}");
        PushLayerRequested?.Invoke(target);
        _preMoveLayer = null;
    }

    private MouseLayerSettings LoadSettingsForProfile(string profileId)
    {
        var s = _settingsService.Load();
        return s.MouseLayer.TryGetValue(profileId, out var loaded)
            ? loaded
            : new MouseLayerSettings();
    }

    private void ReevaluateMonitorState()
    {
        var active = IsActive;
        DiagnosticLog.Info("MouseLayer",
            $"reevaluate: enabled={_settings.Enabled} idx={_settings.MouseLayerIndex} hid={_isHidConnected()} → active={active}");
        if (active) _monitor.Start();
        else _monitor.Stop();
    }

    private void OnMoveStarted()
    {
        if (!IsActive) return;
        var target = _settings.MouseLayerIndex!.Value;
        _preMoveLayer = _getActiveLayer();
        DiagnosticLog.Info("MouseLayer",
            $"move start → push layer {target} (was {_preMoveLayer})");
        PushLayerRequested?.Invoke(target);
    }

    private void OnMoveStopped()
    {
        if (!IsActive) return;
        // EndlessTimeout suppresses the idle-driven revert: the push stays in
        // flight (PreMoveLayer remains populated) until an external trigger
        // (transparent-key tap, profile switch, shutdown) tears it down.
        if (_settings.EndlessTimeout) return;
        // Only OnMoveStarted arms the revert (by capturing _preMoveLayer). If
        // we're idle here it means the push was already reverted out-of-band
        // (settings change / profile switch) — firing layer 0 would jump the
        // keyboard to base unexpectedly.
        if (_preMoveLayer is not int target) return;
        DiagnosticLog.Info("MouseLayer", $"move stop → push layer {target}");
        PushLayerRequested?.Invoke(target);
        _preMoveLayer = null;
    }

    private void PersistSetting(Func<UserSettings, UserSettings> update)
    {
        try
        {
            var s = _settingsService.Load();
            _settingsService.Save(update(s));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("MouseLayer", $"persist failed: {ex.Message}");
        }
    }
}
