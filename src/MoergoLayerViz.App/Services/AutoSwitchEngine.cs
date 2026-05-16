using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Input;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;
using ZmkHidProtocol.Building;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the auto-app→layer state machine. Watches the focused-app stream
/// from <see cref="IActiveWindowMonitor"/>, matches the active window
/// against per-keyboard <see cref="AppLayerRule"/>s, and raises
/// <see cref="PushLayerRequested"/> when the host should change layers.
/// Also owns the exit-tap escape hatch (double-tap of a configured
/// firmware key returns to the fallback layer; second double-tap re-fires
/// the rule).
///
/// <para>The session lifecycle is encoded in the discriminated
/// <see cref="AutoSwitchSession"/> record so impossible states are
/// uncallable — every transition is a single assignment to
/// <see cref="_session"/>.</para>
/// </summary>
public sealed partial class AutoSwitchEngine : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IActiveWindowMonitor? _activeWindowMonitor;
    private readonly Func<int> _getActiveLayer;
    private readonly MultiTapDetector _exitTapDetector = new();

    private IKeyboardProfile _profile;
    private AutoSwitchSession _session = new AutoSwitchSession.Idle();
    private int? _exitTapKey;

    /// <summary>Raised when the engine decides a different layer should be
    /// active on the keyboard. The host applies it via the firmware command
    /// channel (see <c>MainWindowViewModel.PushLayerToKeyboard</c>).</summary>
    public event Action<int>? PushLayerRequested;

    public AutoSwitchEngine(
        ISettingsService settingsService,
        IActiveWindowMonitor? activeWindowMonitor,
        Func<int> getActiveLayer,
        IKeyboardProfile initialProfile)
    {
        _settingsService = settingsService;
        _activeWindowMonitor = activeWindowMonitor;
        _getActiveLayer = getActiveLayer;
        _profile = initialProfile;

        if (_activeWindowMonitor is not null)
        {
            _activeWindow = _activeWindowMonitor.Current;
            _activeWindowMonitor.FocusChanged += HandleActiveWindowFocusChanged;
        }

        AppLayerRules.CollectionChanged += (_, _) =>
        {
            UpdateActiveWindowMonitorGating();
            RecomputeMatchedRule();
        };

        ReloadAppLayerRules();
        ReloadAutoSwitchFallback();
        ReloadExitTapKey();
        _exitTapDetector.Triggered += OnExitTapTriggered;

        RecomputeMatchedRule();

        var s = _settingsService.Load();
        IsEnabled = s.AutoSwitchKeyboardLayer;
        UpdateActiveWindowMonitorGating();
    }

    // ─── Session record ─────────────────────────────────────────────────

    private abstract record AutoSwitchSession
    {
        public sealed record Idle : AutoSwitchSession;

        public sealed record InSession(
            int PreRuleLayer,
            AppLayerRule LastFiredRule,
            int LastPushedLayer,
            bool UserExited) : AutoSwitchSession;
    }

    // ─── Observable state ───────────────────────────────────────────────

    [ObservableProperty]
    private ActiveWindowInfo? _activeWindow;

    partial void OnActiveWindowChanged(ActiveWindowInfo? value) => RecomputeMatchedRule();

    public ObservableCollection<AppLayerRule> AppLayerRules { get; } = new();

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        PersistSetting(s => s with { AutoSwitchKeyboardLayer = value });
        // Clear engine state so re-enabling immediately re-fires the current
        // match, and so disabling doesn't leave stale state.
        _session = new AutoSwitchSession.Idle();
        UpdateActiveWindowMonitorGating();
        if (value) TryFire();
    }

    [ObservableProperty]
    private AutoSwitchFallbackMode _fallbackMode = AutoSwitchFallbackMode.Base;

    partial void OnFallbackModeChanged(AutoSwitchFallbackMode value)
    {
        var profileId = _profile.Id;
        PersistSetting(s =>
        {
            var clone = new Dictionary<string, AutoSwitchFallbackMode>(s.AutoSwitchFallback);
            clone[profileId] = value;
            return s with { AutoSwitchFallback = clone };
        });
    }

    [ObservableProperty]
    private bool _exitOnTransparentKey;

    partial void OnExitOnTransparentKeyChanged(bool value)
    {
        var profileId = _profile.Id;
        PersistSetting(s =>
        {
            var clone = new Dictionary<string, bool>(s.AutoSwitchExitOnTransparent);
            if (value) clone[profileId] = true;
            else clone.Remove(profileId);
            return s with { AutoSwitchExitOnTransparent = clone };
        });
    }

    [ObservableProperty]
    private bool _exitOnEmptyKey;

    partial void OnExitOnEmptyKeyChanged(bool value)
    {
        var profileId = _profile.Id;
        PersistSetting(s =>
        {
            var clone = new Dictionary<string, bool>(s.AutoSwitchExitOnEmpty);
            if (value) clone[profileId] = true;
            else clone.Remove(profileId);
            return s with { AutoSwitchExitOnEmpty = clone };
        });
    }

    private AppLayerRule? _matchedAppLayerRule;
    public AppLayerRule? MatchedAppLayerRule => _matchedAppLayerRule;

    public int? ExitTapKey => _exitTapKey;

    // ─── Public API ─────────────────────────────────────────────────────

    /// <summary>Replaces the exit-tap key for the active profile, persists,
    /// and re-arms the detector.</summary>
    public void SetExitTapKey(int? index)
    {
        var clean = index is int i && i >= 0 ? i : (int?)null;

        _exitTapKey = clean;
        _exitTapDetector.SetPosition(clean);
        OnPropertyChanged(nameof(ExitTapKey));

        var profileId = _profile.Id;
        PersistSetting(s =>
        {
            var clone = new Dictionary<string, int>(s.AutoSwitchExitKey);
            if (clean is null) clone.Remove(profileId);
            else clone[profileId] = clean.Value;
            return s with { AutoSwitchExitKey = clone };
        });
    }

    /// <summary>Replaces the active profile's rule list with
    /// <paramref name="rules"/> in one shot and persists. Called by
    /// <c>SettingsViewModel</c> on settings-window close — CRUD lives there,
    /// against an edit buffer, and the committed snapshot flows in here.</summary>
    public void ApplyAppLayerRules(IReadOnlyList<AppLayerRule> rules)
    {
        AppLayerRules.Clear();
        foreach (var r in rules) AppLayerRules.Add(r);
        PersistAppLayerRules();
    }

    /// <summary>Switches active keyboard profile. Reloads per-profile rules,
    /// fallback mode, and exit-tap key; invalidates the session (snapshot
    /// was captured against the previous keyboard's layer indices).</summary>
    public void SetActiveProfile(IKeyboardProfile profile)
    {
        _profile = profile;
        ReloadAppLayerRules();
        ReloadAutoSwitchFallback();
        ReloadExitTapKey();
        _session = new AutoSwitchSession.Idle();
    }

    /// <summary>Feeds a firmware key-position event into the exit-tap
    /// detector. Safe to call when no exit key is configured (detector
    /// short-circuits internally).</summary>
    public void OnKeyPositionEvent(int position, bool pressed)
        => _exitTapDetector.OnKeyEvent(position, pressed);

    /// <summary>
    /// Pushes the configured fallback layer and marks the current session as
    /// user-exited (so a subsequent focus change to the same rule won't auto
    /// re-fire). No-op when the engine isn't in a session or when the user
    /// has already exited this session. Mirrors the first branch of the
    /// double-tap exit handler — extracted so the coordinator can trigger
    /// the same exit from a transparent-key tap. Marshals to the UI thread.
    /// </summary>
    public void ExitToFallback(string reason)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsEnabled) return;
            if (_session is not AutoSwitchSession.InSession s) return;
            if (s.UserExited) return;
            int target = GetFallbackTargetLayer(s.PreRuleLayer);
            int activeLayer = _getActiveLayer();
            if (target != activeLayer)
                PushLayerRequested?.Invoke(target);
            _session = s with { UserExited = true };
            DiagnosticLog.Info("AutoSwitch",
                $"{reason}: fell back to layer {target} (rule was '{s.LastFiredRule.ProcessMatch}' → {s.LastFiredRule.LayerIndex})");
        });
    }

    /// <summary>Detaches from the monitor + detector. Idempotent.</summary>
    public void Shutdown()
    {
        if (_activeWindowMonitor is not null)
            _activeWindowMonitor.FocusChanged -= HandleActiveWindowFocusChanged;
        _exitTapDetector.Triggered -= OnExitTapTriggered;
    }

    // ─── Internals ──────────────────────────────────────────────────────

    private void HandleActiveWindowFocusChanged(ActiveWindowInfo info)
    {
        // Monitor polls on a background thread; marshal so PropertyChanged
        // reaches XAML bindings on the UI thread.
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ActiveWindow = info);
    }

    private void RecomputeMatchedRule()
    {
        var newMatch = AppLayerRuleMatcher.Match(ActiveWindow, AppLayerRules);
        if (Equals(_matchedAppLayerRule, newMatch)) return;
        _matchedAppLayerRule = newMatch;
        OnPropertyChanged(nameof(MatchedAppLayerRule));
        TryFire();
    }

    private void ReloadAppLayerRules()
    {
        AppLayerRules.Clear();
        var s = _settingsService.Load();
        if (s.AppLayerRules.TryGetValue(_profile.Id, out var list))
            foreach (var r in list) AppLayerRules.Add(r);
    }

    private void PersistAppLayerRules()
    {
        var profileId = _profile.Id;
        var snapshot = AppLayerRules.ToList();
        PersistSetting(s =>
        {
            var clone = new Dictionary<string, List<AppLayerRule>>();
            foreach (var (pid, list) in s.AppLayerRules)
                clone[pid] = new List<AppLayerRule>(list);
            if (snapshot.Count == 0) clone.Remove(profileId);
            else clone[profileId] = snapshot;
            return s with { AppLayerRules = clone };
        });
    }

    private void ReloadAutoSwitchFallback()
    {
        var s = _settingsService.Load();
        FallbackMode = s.AutoSwitchFallback.TryGetValue(_profile.Id, out var m)
            ? m
            : AutoSwitchFallbackMode.Base;
        ExitOnTransparentKey = s.AutoSwitchExitOnTransparent.TryGetValue(_profile.Id, out var t) && t;
        ExitOnEmptyKey = s.AutoSwitchExitOnEmpty.TryGetValue(_profile.Id, out var e) && e;
    }

    private void ReloadExitTapKey()
    {
        var s = _settingsService.Load();
        int? key = s.AutoSwitchExitKey.TryGetValue(_profile.Id, out var stored) && stored >= 0
            ? stored
            : (int?)null;
        _exitTapKey = key;
        _exitTapDetector.SetPosition(key);
        OnPropertyChanged(nameof(ExitTapKey));
    }

    private int GetFallbackTargetLayer(int preRuleLayer) =>
        FallbackMode == AutoSwitchFallbackMode.Base ? 0 : preRuleLayer;

    private int? ResolveMouseLayerIndex()
    {
        var s = _settingsService.Load();
        return s.MouseLayer.TryGetValue(_profile.Id, out var ms) ? ms.MouseLayerIndex : null;
    }

    /// <summary>
    /// Auto-switch state transition. Fires on every
    /// <see cref="MatchedAppLayerRule"/> change.
    /// </summary>
    private void TryFire()
    {
        if (!IsEnabled) return;
        var match = MatchedAppLayerRule;
        int activeLayer = _getActiveLayer();

        switch (_session, match)
        {
            case (AutoSwitchSession.Idle, null):
                return;

            case (AutoSwitchSession.Idle, not null):
                // Snapshot the layer that was active when the matching app
                // gained focus — but if the user opened the app *with* the
                // mouse, MouseLayerEngine has just put us on the configured
                // mouse layer, which is a nogo as a typing fallback. Treat
                // that case as Base.
                int captured = activeLayer == ResolveMouseLayerIndex() ? 0 : activeLayer;
                _session = new AutoSwitchSession.InSession(
                    PreRuleLayer: captured,
                    LastFiredRule: match,
                    LastPushedLayer: match.LayerIndex,
                    UserExited: false);
                PushLayerRequested?.Invoke(match.LayerIndex);
                return;

            case (AutoSwitchSession.InSession s, null):
                bool userOverrode = activeLayer != s.LastPushedLayer;
                if (!userOverrode && !s.UserExited)
                {
                    int target = GetFallbackTargetLayer(s.PreRuleLayer);
                    if (target != activeLayer)
                        PushLayerRequested?.Invoke(target);
                }
                _session = new AutoSwitchSession.Idle();
                return;

            case (AutoSwitchSession.InSession s, not null):
                if (match.Equals(s.LastFiredRule) && !s.UserExited) return;
                PushLayerRequested?.Invoke(match.LayerIndex);
                _session = s with
                {
                    LastFiredRule = match,
                    LastPushedLayer = match.LayerIndex,
                    UserExited = false,
                };
                return;
        }
    }

    private void OnExitTapTriggered()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsEnabled) return;
            if (_session is not AutoSwitchSession.InSession s) return;
            var match = MatchedAppLayerRule;
            if (match is null) return;

            if (!s.UserExited)
            {
                // ExitToFallback re-posts to the UI thread, which is a no-op
                // when already on it. Cheaper than duplicating the body here.
                ExitToFallback("exit tap");
                return;
            }

            int activeLayer = _getActiveLayer();
            if (match.LayerIndex != activeLayer)
                PushLayerRequested?.Invoke(match.LayerIndex);
            _session = s with
            {
                LastFiredRule = match,
                LastPushedLayer = match.LayerIndex,
                UserExited = false,
            };
            DiagnosticLog.Info("AutoSwitch", $"exit tap: re-entered rule '{match.ProcessMatch}' → layer {match.LayerIndex}");
        });
    }

    private void UpdateActiveWindowMonitorGating()
    {
        var monitor = _activeWindowMonitor;
        if (monitor is null) return;
        bool shouldRun = IsEnabled && AppLayerRules.Count > 0;
        if (shouldRun) monitor.Start();
        else monitor.Stop();
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
            DiagnosticLog.Warn("AutoSwitch", $"Persist failed: {ex.Message}");
        }
    }
}
