namespace MoergoLayerViz.App.Services.MouseIdle;

/// <summary>
/// Cross-platform global mouse-movement / idle detector. Implementations tap
/// the OS-level pointer movement stream (NSEvent global+local monitors on
/// macOS, <c>WH_MOUSE_LL</c> hook on Windows, no-op stub on Linux) and emit
/// <see cref="MoveStarted"/> / <see cref="MoveStopped"/> based on whether
/// any movement was observed within the trailing <see cref="IdleTimeoutMs"/>
/// window.
///
/// Events fire on the monitor's internal timer thread; consumers that touch
/// UI state must marshal to the dispatcher themselves.
/// </summary>
public interface IMouseIdleMonitor : IDisposable
{
    /// <summary>Raised the first tick after motion is detected while in the Idle state.</summary>
    event Action? MoveStarted;

    /// <summary>Raised the first tick after no motion has been observed for <see cref="IdleTimeoutMs"/>.</summary>
    event Action? MoveStopped;

    /// <summary>
    /// Trailing window (ms) of "no movement" required to flip from Moving →
    /// Idle. Writes are observed on the next state-machine tick. The Settings
    /// UI clamps the bound value to 200–2000 ms; the monitor itself accepts
    /// any positive value for testing.
    /// </summary>
    int IdleTimeoutMs { get; set; }

    /// <summary>
    /// Installs the OS-level event tap and starts the state-machine timer.
    /// Idempotent — repeated calls have no effect once started.
    /// </summary>
    void Start();

    /// <summary>
    /// Tears down the OS-level event tap and stops the state-machine timer.
    /// Idempotent. No <see cref="MoveStopped"/> event is raised on Stop —
    /// callers that need a final revert must do it themselves.
    /// </summary>
    void Stop();
}
