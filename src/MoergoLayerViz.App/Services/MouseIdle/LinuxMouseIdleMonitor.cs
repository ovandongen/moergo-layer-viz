using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.MouseIdle;

/// <summary>
/// Linux implementation. Polls the root-window pointer position via
/// <c>XQueryPointer</c> on the XWayland display (<c>DISPLAY</c> env var)
/// once per state-machine tick. No grab or accessibility permission is
/// required — <c>XQueryPointer</c> on the root window is always allowed.
///
/// Works on both X11 and Wayland-with-XWayland sessions. If no DISPLAY is
/// available (pure Wayland without XWayland) <see cref="StartPlatformMonitor"/>
/// logs a warning and the monitor degrades to the silent no-op posture.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxMouseIdleMonitor : MouseIdleMonitorBase
{
    private const string LibX11 = "libX11.so.6";

    private IntPtr _display;
    private int _lastX;
    private int _lastY;
    private bool _hasSample;

    protected override void StartPlatformMonitor()
    {
        _display = XOpenDisplay(null);
        if (_display == IntPtr.Zero)
            DiagnosticLog.Warn("MouseIdle/Linux", "XOpenDisplay failed; mouse idle tracking disabled.");
        _hasSample = false;
    }

    protected override void StopPlatformMonitor()
    {
        if (_display != IntPtr.Zero)
        {
            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
        _hasSample = false;
    }

    protected override void OnPollTick()
    {
        if (_display == IntPtr.Zero) return;

        var root = XDefaultRootWindow(_display);
        var ok = XQueryPointer(_display, root,
            out _, out _,
            out int rootX, out int rootY,
            out _, out _, out _);

        if (!ok) return;

        if (_hasSample && (rootX != _lastX || rootY != _lastY))
            NotifyMovement();

        _lastX = rootX;
        _lastY = rootY;
        _hasSample = true;
    }

    [DllImport(LibX11)] private static extern IntPtr XOpenDisplay(string? display);
    [DllImport(LibX11)] private static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport(LibX11)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool XQueryPointer(
        IntPtr display, IntPtr w,
        out IntPtr root_return, out IntPtr child_return,
        out int root_x_return, out int root_y_return,
        out int win_x_return, out int win_y_return,
        out uint mask_return);
}
