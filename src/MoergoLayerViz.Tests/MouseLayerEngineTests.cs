using MoergoLayerViz.App.Services;
using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Settings;
using Xunit;

namespace MoergoLayerViz.Tests;

public class MouseLayerEngineTests
{
    private sealed class InMemorySettingsService : ISettingsService
    {
        public UserSettings Current { get; set; } = new();
        public UserSettings Load() => Current;
        public void Save(UserSettings settings) => Current = settings;
    }

    /// <summary>
    /// Manually-driveable monitor — tests call <see cref="FireMoveStarted"/> /
    /// <see cref="FireMoveStopped"/> to simulate platform-tap output without a
    /// real timer or OS hook. Mirrors the FakeRegistry pattern used by the
    /// hotkey tests.
    /// </summary>
    private sealed class FakeMouseIdleMonitor : IMouseIdleMonitor
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public bool Running { get; private set; }
        public int IdleTimeoutMs { get; set; }

        public event Action? MoveStarted;
        public event Action? MoveStopped;

        public void Start() { Running = true; StartCount++; }
        public void Stop() { Running = false; StopCount++; }
        public void Dispose() { }

        public void FireMoveStarted() => MoveStarted?.Invoke();
        public void FireMoveStopped() => MoveStopped?.Invoke();
    }

    private static MouseLayerEngine NewEngine(
        MouseLayerSettings initialSettings,
        FakeMouseIdleMonitor monitor,
        Func<int> getActiveLayer,
        Func<bool> isHidConnected,
        out List<int> pushes,
        out InMemorySettingsService settingsService)
    {
        var profile = new Go60Profile();
        settingsService = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                MouseLayer = new Dictionary<string, MouseLayerSettings>
                {
                    [profile.Id] = initialSettings,
                },
            },
        };
        var engine = new MouseLayerEngine(
            settingsService, monitor, getActiveLayer, isHidConnected, profile);
        var captured = new List<int>();
        engine.PushLayerRequested += captured.Add;
        pushes = captured;
        return engine;
    }

    [Fact]
    public void Disabled_DoesNotStartMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(Enabled: false, MouseLayerIndex: 3),
            monitor, () => 0, () => true, out var pushes, out _);

        Assert.False(monitor.Running);
        Assert.Equal(0, monitor.StartCount);
        Assert.Empty(pushes);
    }

    [Fact]
    public void EnabledWithNullLayer_DoesNotStartMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(Enabled: true, MouseLayerIndex: null),
            monitor, () => 0, () => true, out var pushes, out _);

        Assert.False(monitor.Running);
        Assert.Empty(pushes);
    }

    [Fact]
    public void EnabledButHidDisconnected_DoesNotStartMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3),
            monitor, () => 0, () => false, out var pushes, out _);

        Assert.False(monitor.Running);
        Assert.Empty(pushes);
    }

    [Fact]
    public void FullyConfigured_StartsMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3),
            monitor, () => 0, () => true, out _, out _);

        Assert.True(monitor.Running);
        Assert.Equal(1, monitor.StartCount);
    }

    [Fact]
    public void MoveStart_PushesConfiguredLayer()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4),
            monitor, () => 1, () => true, out var pushes, out _);

        monitor.FireMoveStarted();

        Assert.Equal(new[] { 4 }, pushes);
    }

    [Fact]
    public void MoveStop_PushesCapturedPreMoveLayer()
    {
        var monitor = new FakeMouseIdleMonitor();
        var activeLayer = 2;
        NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4),
            monitor, () => activeLayer, () => true, out var pushes, out _);

        monitor.FireMoveStarted();   // captures pre-move = 2, pushes 4
        activeLayer = 4;             // pretend HID acked the push
        monitor.FireMoveStopped();   // reverts to pre-move = 2

        Assert.Equal(new[] { 4, 2 }, pushes);
    }

    [Fact]
    public void TryRedirectPendingPush_WhilePushing_UpdatesPreMoveAndReturnsTrue()
    {
        var monitor = new FakeMouseIdleMonitor();
        var engine = NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4),
            monitor, () => 1, () => true, out var pushes, out _);

        monitor.FireMoveStarted();           // captures pre-move = 1, pushes 4
        var accepted = engine.TryRedirectPendingPush(2);
        monitor.FireMoveStopped();           // reverts to redirected pre-move = 2

        Assert.True(accepted);
        Assert.Equal(new[] { 4, 2 }, pushes);
    }

    [Fact]
    public void TryRedirectPendingPush_WhenNotPushing_ReturnsFalseAndDoesNotPush()
    {
        var monitor = new FakeMouseIdleMonitor();
        var engine = NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4),
            monitor, () => 1, () => true, out var pushes, out _);

        var accepted = engine.TryRedirectPendingPush(2);

        Assert.False(accepted);
        Assert.Empty(pushes);
        Assert.Null(engine.PreMoveLayer);
    }

    [Fact]
    public void ApplySettings_PersistsToSettingsService_KeyedByProfileId()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(), monitor, () => 0, () => true,
                  out _, out var settings);

        var next = new MouseLayerSettings(Enabled: true, MouseLayerIndex: 5, IdleTimeoutMs: 750);
        // Reach through the engine the same way the SettingsViewModel does.
        // The engine isn't exposed via a static facade here, so trigger via
        // OnHidConnectionChanged-driven side effects: easiest is to use the
        // public ApplySettings method.
        var profile = new Go60Profile();
        var engineWithReload = new MouseLayerEngine(
            settings, monitor, () => 0, () => true, profile);
        engineWithReload.ApplySettings(next);

        Assert.True(settings.Current.MouseLayer.ContainsKey(profile.Id));
        Assert.Equal(next, settings.Current.MouseLayer[profile.Id]);
    }

    [Fact]
    public void ApplySettings_PropagatesIdleTimeoutToMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        NewEngine(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3, IdleTimeoutMs: 500),
            monitor, () => 0, () => true, out _, out _);

        Assert.Equal(500, monitor.IdleTimeoutMs);

        // The engine instance used above is local; the test instead uses a
        // freshly-built engine to exercise ApplySettings.
        var profile = new Go60Profile();
        var settings = new InMemorySettingsService();
        var engine = new MouseLayerEngine(settings, monitor, () => 0, () => true, profile);
        engine.ApplySettings(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3,
                                                   IdleTimeoutMs: 1200));

        Assert.Equal(1200, monitor.IdleTimeoutMs);
    }

    [Fact]
    public void ApplySettings_FlippingEnabledOff_StopsMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        var profile = new Go60Profile();
        var settings = new InMemorySettingsService();
        var engine = new MouseLayerEngine(settings, monitor, () => 0, () => true, profile);
        engine.ApplySettings(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3));
        Assert.True(monitor.Running);

        engine.ApplySettings(new MouseLayerSettings(Enabled: false, MouseLayerIndex: 3));

        Assert.False(monitor.Running);
    }

    [Fact]
    public void OnHidConnectionChanged_StartsAndStopsMonitor()
    {
        var monitor = new FakeMouseIdleMonitor();
        var profile = new Go60Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                MouseLayer = new Dictionary<string, MouseLayerSettings>
                {
                    [profile.Id] = new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3),
                },
            },
        };
        var hidConnected = false;
        var engine = new MouseLayerEngine(settings, monitor, () => 0, () => hidConnected, profile);
        Assert.False(monitor.Running);

        hidConnected = true;
        engine.OnHidConnectionChanged();
        Assert.True(monitor.Running);

        hidConnected = false;
        engine.OnHidConnectionChanged();
        Assert.False(monitor.Running);
    }

    [Fact]
    public void MoveStop_AfterApplySettingsClearedPush_DoesNotPushFallbackZero()
    {
        // Regression: ApplySettings mid-push calls RevertIfPushed, which clears
        // _preMoveLayer. If the monitor is still active and a stale FireMoveStopped
        // arrives, the engine used to fall back to layer 0 and silently jump the
        // keyboard to base. The Start path is the only thing that should arm Stop.
        var monitor = new FakeMouseIdleMonitor();
        var engine = NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4),
            monitor, () => 2, () => true, out var pushes, out _);

        monitor.FireMoveStarted();  // captures pre-move = 2, pushes 4
        engine.ApplySettings(new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4, IdleTimeoutMs: 800));
        // Revert from settings change pushed 2 back; engine state is now idle.
        var pushesBeforeStaleStop = pushes.Count;

        monitor.FireMoveStopped();  // stale event — must not push anything

        Assert.Equal(pushesBeforeStaleStop, pushes.Count);
    }

    [Fact]
    public void EndlessTimeout_MoveStop_DoesNotPushAndKeepsPreMoveLayer()
    {
        var monitor = new FakeMouseIdleMonitor();
        var engine = NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4, EndlessTimeout: true),
            monitor, () => 2, () => true, out var pushes, out _);

        monitor.FireMoveStarted();   // captures pre-move = 2, pushes 4
        monitor.FireMoveStopped();   // endless → suppressed

        Assert.Equal(new[] { 4 }, pushes);
        Assert.Equal(2, engine.PreMoveLayer);
    }

    [Fact]
    public void RevertNow_WhilePushing_FiresCapturedPreMoveAndClears()
    {
        var monitor = new FakeMouseIdleMonitor();
        var engine = NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4, EndlessTimeout: true),
            monitor, () => 2, () => true, out var pushes, out _);

        monitor.FireMoveStarted();          // pushes 4, pre-move=2
        engine.RevertNow("test");           // pushes 2, clears

        Assert.Equal(new[] { 4, 2 }, pushes);
        Assert.Null(engine.PreMoveLayer);
    }

    [Fact]
    public void RevertNow_WithoutPush_IsNoOp()
    {
        var monitor = new FakeMouseIdleMonitor();
        var engine = NewEngine(
            new MouseLayerSettings(Enabled: true, MouseLayerIndex: 4),
            monitor, () => 2, () => true, out var pushes, out _);

        engine.RevertNow("test");

        Assert.Empty(pushes);
        Assert.Null(engine.PreMoveLayer);
    }

    [Fact]
    public void SetActiveProfile_ReloadsSettingsForNewProfile()
    {
        var monitor = new FakeMouseIdleMonitor();
        var go60 = new Go60Profile();
        var glove = new Glove80Profile();
        var settings = new InMemorySettingsService
        {
            Current = new UserSettings
            {
                MouseLayer = new Dictionary<string, MouseLayerSettings>
                {
                    [go60.Id] = new MouseLayerSettings(Enabled: true, MouseLayerIndex: 3),
                    [glove.Id] = new MouseLayerSettings(Enabled: false, MouseLayerIndex: 7),
                },
            },
        };
        var engine = new MouseLayerEngine(settings, monitor, () => 0, () => true, go60);
        Assert.True(monitor.Running);
        Assert.Equal(3, engine.CurrentSettings.MouseLayerIndex);

        engine.SetActiveProfile(glove);

        Assert.False(monitor.Running);
        Assert.Equal(7, engine.CurrentSettings.MouseLayerIndex);
        Assert.False(engine.CurrentSettings.Enabled);
    }
}

public class IdleStateMachineTests
{
    /// <summary>
    /// Minimal subclass that exposes a no-op platform layer so we can drive
    /// <see cref="MouseIdleMonitorBase"/>'s state machine via its internal
    /// test hooks without spinning a real timer or installing an OS tap.
    /// </summary>
    private sealed class TestableMonitor : MouseIdleMonitorBase
    {
        protected override void StartPlatformMonitor() { }
        protected override void StopPlatformMonitor() { }
    }

    [Fact]
    public void Tick_BeforeStart_DoesNothing()
    {
        var m = new TestableMonitor();
        var started = 0;
        m.MoveStarted += () => started++;

        m.NotifyMovementAt(1000);
        m.Tick(1010);

        Assert.Equal(0, started);
    }

    [Fact]
    public void Movement_WithinTimeout_FiresMoveStarted()
    {
        var m = new TestableMonitor { IdleTimeoutMs = 500 };
        var started = 0;
        m.MoveStarted += () => started++;
        m.Start();

        m.NotifyMovementAt(1000);
        m.Tick(1100); // 100 ms since last move < 500 → Moving

        Assert.Equal(1, started);

        m.Stop();
    }

    [Fact]
    public void IdleBeyondTimeout_FiresMoveStopped()
    {
        var m = new TestableMonitor { IdleTimeoutMs = 500 };
        var started = 0;
        var stopped = 0;
        m.MoveStarted += () => started++;
        m.MoveStopped += () => stopped++;
        m.Start();

        m.NotifyMovementAt(1000);
        m.Tick(1100);  // enter Moving
        m.Tick(1700);  // 700 ms since last move ≥ 500 → Idle

        Assert.Equal(1, started);
        Assert.Equal(1, stopped);

        m.Stop();
    }

    [Fact]
    public void RepeatedMovement_KeepsStateMoving_NoDuplicateStartedEvents()
    {
        var m = new TestableMonitor { IdleTimeoutMs = 500 };
        var started = 0;
        m.MoveStarted += () => started++;
        m.Start();

        m.NotifyMovementAt(1000);
        m.Tick(1050);

        m.NotifyMovementAt(1100);
        m.Tick(1150);

        m.NotifyMovementAt(1200);
        m.Tick(1250);

        Assert.Equal(1, started);

        m.Stop();
    }

    [Fact]
    public void MoveStartedAndStopped_AlternateAcrossCycles()
    {
        var m = new TestableMonitor { IdleTimeoutMs = 300 };
        var events = new List<string>();
        m.MoveStarted += () => events.Add("start");
        m.MoveStopped += () => events.Add("stop");
        m.Start();

        m.NotifyMovementAt(1000); m.Tick(1100);  // start (delta 100 < 300)
        m.Tick(1500);                            // stop  (delta 500 ≥ 300)
        m.NotifyMovementAt(2000); m.Tick(2050);  // start
        m.Tick(2400);                            // stop  (delta 400 ≥ 300)

        Assert.Equal(new[] { "start", "stop", "start", "stop" }, events);

        m.Stop();
    }
}
