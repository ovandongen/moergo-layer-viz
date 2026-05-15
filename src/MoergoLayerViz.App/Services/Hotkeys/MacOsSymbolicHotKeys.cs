using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Reads macOS's currently-active symbolic hotkey table via Carbon's
/// <c>CopySymbolicHotKeys</c>. The returned set is which F-key indices are
/// claimed as a bare F-key (no Cmd / Shift / Alt / Ctrl). Bare F-keys in
/// this set can still be registered with <c>RegisterEventHotKey</c>
/// (status 0), but the system handler pre-empts the press, so our
/// <c>kEventHotKeyPressed</c> handler is never invoked. Surface these to
/// the picker so users don't bind them by mistake.
///
/// The data is live (defaults merged with user overrides) — but sampled
/// once at app startup. Changing system shortcuts mid-session requires an
/// app restart for the picker to refresh.
/// </summary>
[SupportedOSPlatform("macos")]
internal static class MacOsSymbolicHotKeys
{
    // Carbon modifier flags (Events.h). The "function" bit (0x800000) is
    // intentionally not in this mask — macOS reports F-keys with the
    // function bit set, and we want to flag those as bare-F-key claims.
    private const int UserModifierMask =
        (1 << 8)   // cmdKey
      | (1 << 9)   // shiftKey
      | (1 << 11)  // optionKey
      | (1 << 12); // controlKey

    /// <summary>
    /// F-key indices (1..20) currently claimed by a bare F-key system
    /// shortcut. Empty if the probe fails — better to show everything than
    /// hide based on a guess.
    /// </summary>
    public static IReadOnlySet<int> GetClaimedFKeyIndices()
    {
        var claimed = new HashSet<int>();
        var array = IntPtr.Zero;
        var enabledKey = IntPtr.Zero;
        var codeKey = IntPtr.Zero;
        var modsKey = IntPtr.Zero;
        try
        {
            var status = CopySymbolicHotKeys(out array);
            if (status != 0 || array == IntPtr.Zero)
            {
                DiagnosticLog.Warn("HotkeyMac", $"CopySymbolicHotKeys failed (status {status}, array=0x{array.ToInt64():X})");
                return claimed;
            }

            enabledKey = CreateCFString("enabled");
            codeKey = CreateCFString("key");
            modsKey = CreateCFString("mod");

            var count = CFArrayGetCount(array);
            for (long i = 0; i < count; i++)
            {
                var dict = CFArrayGetValueAtIndex(array, i);
                if (dict == IntPtr.Zero) continue;

                if (!TryGetBool(dict, enabledKey, out var enabled) || !enabled) continue;
                if (!TryGetInt32(dict, codeKey, out var vk)) continue;
                if (!TryGetInt32(dict, modsKey, out var mods)) continue;
                if ((mods & UserModifierMask) != 0) continue;

                if (MacOsFKeyMap.GetFIndex(vk) is { } fIndex)
                    claimed.Add(fIndex);
            }
            DiagnosticLog.Info("HotkeyMac",
                $"CopySymbolicHotKeys: {count} entries, claimed F-keys: [{string.Join(",", claimed.OrderBy(x => x))}]");
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("HotkeyMac", $"CopySymbolicHotKeys probe failed: {ex.Message}");
        }
        finally
        {
            if (enabledKey != IntPtr.Zero) CFRelease(enabledKey);
            if (codeKey != IntPtr.Zero) CFRelease(codeKey);
            if (modsKey != IntPtr.Zero) CFRelease(modsKey);
            if (array != IntPtr.Zero) CFRelease(array);
        }
        return claimed;
    }

    private static IntPtr CreateCFString(string s) =>
        CFStringCreateWithCString(IntPtr.Zero, s, kCFStringEncodingUTF8);

    private static bool TryGetBool(IntPtr dict, IntPtr key, out bool value)
    {
        value = false;
        if (!CFDictionaryGetValueIfPresent(dict, key, out var v) || v == IntPtr.Zero) return false;
        value = CFBooleanGetValue(v);
        return true;
    }

    private static bool TryGetInt32(IntPtr dict, IntPtr key, out int value)
    {
        value = 0;
        if (!CFDictionaryGetValueIfPresent(dict, key, out var v) || v == IntPtr.Zero) return false;
        return CFNumberGetValue(v, kCFNumberSInt32Type, out value);
    }

    private const long kCFNumberSInt32Type = 3;
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // Signature: OSStatus CopySymbolicHotKeys(CFArrayRef *outHotKeys).
    // Returning the array via the return-value slot crashes HIToolbox with
    // SIGBUS — it expects to write through an out-pointer arg.
    [DllImport(Carbon)]
    private static extern int CopySymbolicHotKeys(out IntPtr outHotKeys);

    [DllImport(CoreFoundation)]
    private static extern void CFRelease(IntPtr cf);

    [DllImport(CoreFoundation)]
    private static extern long CFArrayGetCount(IntPtr theArray);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, long idx);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFDictionaryGetValueIfPresent(IntPtr theDict, IntPtr key, out IntPtr value);

    [DllImport(CoreFoundation)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr, uint encoding);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFBooleanGetValue(IntPtr boolean);

    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(IntPtr number, long theType, out int valuePtr);
}
