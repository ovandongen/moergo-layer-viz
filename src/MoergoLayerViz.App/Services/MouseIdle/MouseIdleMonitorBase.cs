using System.Diagnostics;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.MouseIdle;

/// <summary>
/// Shared idle-detection state machine for the platform-specific monitors.
/// Platform impls (<see cref="MacOsMouseIdleMonitor"/>,
/// <see cref="WindowsMouseIdleMonitor"/>) install their OS event tap in
/// <see cref="StartPlatformMonitor"/> / <see cref="StopPlatformMonitor"/>
/// and call <see cref="NotifyMovement"/> from the OS handler. This base owns
/// the timer + Idle/Moving state flips so platform code stays minimal and
/// the timing semantics are testable from a single place.
///
/// Pure state-machine variant exposed via the <c>internal</c>
/// <see cref="Tick(long)"/> hook so tests can drive movement timestamps and
/// the clock without spinning a real timer.
/// </summary>
public abstract class MouseIdleMonitorBase : IMouseIdleMonitor
{
    /// <summary>Polling interval of the state-machine tick (ms).</summary>
    private const int TickIntervalMs = 100;

    private readonly object _lock = new();
    private System.Threading.Timer? _timer;
    private long _lastMoveTicks;
    private bool _moving;
    private bool _started;
    private bool _disposed;

    public event Action? MoveStarted;
    public event Action? MoveStopped;

    public int IdleTimeoutMs { get; set; } = 500;

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed || _started) return;
            _started = true;
            _lastMoveTicks = NowMs();
            _moving = false;
            StartPlatformMonitor();
            _timer = new System.Threading.Timer(OnTick, null, TickIntervalMs, TickIntervalMs);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_started) return;
            _started = false;
            _timer?.Dispose();
            _timer = null;
            StopPlatformMonitor();
            _moving = false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Called by platform impls from their OS handler whenever any mouse
    /// movement event arrives. Allocation-free; no logging — this can fire
    /// at the OS pointer-event rate.
    /// </summary>
    protected void NotifyMovement()
    {
        // Single 64-bit write; reads in Tick are also atomic on the platforms
        // we target so no Interlocked is needed.
        Volatile.Write(ref _lastMoveTicks, NowMs());
    }

    /// <summary>Installs the OS-level event tap. Called under <c>_lock</c>.</summary>
    protected abstract void StartPlatformMonitor();

    /// <summary>Tears down the OS-level event tap. Called under <c>_lock</c>.</summary>
    protected abstract void StopPlatformMonitor();

    /// <summary>
    /// Per-tick poll hook. Push-based platforms (Windows
    /// <c>WH_MOUSE_LL</c>) leave this empty; poll-based platforms (macOS,
    /// where avoiding TCC prompts means we can't install a global event tap)
    /// override it to sample mouse position and call <see cref="NotifyMovement"/>
    /// when it has changed since the previous tick.
    /// </summary>
    protected virtual void OnPollTick() { }

    private void OnTick(object? state)
    {
        try { OnPollTick(); }
        catch (Exception ex) { DiagnosticLog.Warn("MouseIdle", $"poll tick threw: {ex.Message}"); }
        Tick(NowMs());
    }

    /// <summary>
    /// Pure state-machine step. Internal so tests can drive timestamps
    /// without a real timer or platform tap.
    /// </summary>
    internal void Tick(long nowMs)
    {
        bool fireStart = false;
        bool fireStop = false;
        lock (_lock)
        {
            if (!_started) return;
            var sinceLast = nowMs - Volatile.Read(ref _lastMoveTicks);
            if (!_moving && sinceLast < IdleTimeoutMs)
            {
                _moving = true;
                fireStart = true;
            }
            else if (_moving && sinceLast >= IdleTimeoutMs)
            {
                _moving = false;
                fireStop = true;
            }
        }
        // Raise outside the lock so handlers can call Stop / Dispose freely.
        if (fireStart) MoveStarted?.Invoke();
        if (fireStop) MoveStopped?.Invoke();
    }

    /// <summary>
    /// Test hook: simulate a movement timestamp without going through the
    /// platform tap. Mirrors <see cref="NotifyMovement"/> with a caller-
    /// supplied clock value.
    /// </summary>
    internal void NotifyMovementAt(long nowMs) => Volatile.Write(ref _lastMoveTicks, nowMs);

    private static long NowMs() => Stopwatch.GetTimestamp() * 1000L / Stopwatch.Frequency;
}
