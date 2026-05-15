namespace MoergoLayerViz.App.Services.MouseIdle;

/// <summary>
/// Linux placeholder. Capturing global pointer movement on Linux requires
/// per-WM cooperation (Wayland portals, X11 XInput grabs) that we don't
/// ship today, so the Linux build silently no-ops: <see cref="Start"/> /
/// <see cref="Stop"/> succeed but <see cref="MoveStarted"/> /
/// <see cref="MoveStopped"/> never fire. Matches the Linux posture of
/// <see cref="Hotkeys.LinuxHotkeyRegistry"/>.
/// </summary>
public sealed class LinuxMouseIdleMonitor : IMouseIdleMonitor
{
    public event Action? MoveStarted { add { } remove { } }
    public event Action? MoveStopped { add { } remove { } }
    public int IdleTimeoutMs { get; set; } = 500;
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}
