using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace MoergoLayerViz.App.Services.MouseIdle;

/// <summary>
/// macOS implementation. Polls <c>CGEventGetLocation(CGEventCreate(NULL))</c>
/// once per state-machine tick. Both calls are documented as TCC-free —
/// they query the current mouse position without installing an event tap or
/// requesting Accessibility / Input-Monitoring permission.
///
/// Picked over <c>NSEvent.addGlobalMonitorForEventsMatchingMask:</c> to avoid
/// having to construct an Objective-C block from C# (the standard NSEvent
/// monitor pattern), and over <c>CGEventTapCreate</c> because tap creation
/// triggers the Input-Monitoring prompt on first use.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacOsMouseIdleMonitor : MouseIdleMonitorBase
{
    private CGPoint _lastSampled;
    private bool _hasSample;

    protected override void StartPlatformMonitor()
    {
        _hasSample = false;
    }

    protected override void StopPlatformMonitor() { }

    protected override void OnPollTick()
    {
        var ev = CGEventCreate(IntPtr.Zero);
        if (ev == IntPtr.Zero) return;
        try
        {
            var pt = CGEventGetLocation(ev);
            if (_hasSample && (pt.X != _lastSampled.X || pt.Y != _lastSampled.Y))
                NotifyMovement();
            _lastSampled = pt;
            _hasSample = true;
        }
        finally
        {
            CFRelease(ev);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    private const string ApplicationServices =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreFoundation =
        "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(ApplicationServices)]
    private static extern IntPtr CGEventCreate(IntPtr source);

    [DllImport(ApplicationServices)]
    private static extern CGPoint CGEventGetLocation(IntPtr eventRef);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);
}
