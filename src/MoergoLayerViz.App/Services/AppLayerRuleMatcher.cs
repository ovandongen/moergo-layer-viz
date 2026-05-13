using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Shared substring-match between the focused-window process name and the
/// configured <see cref="AppLayerRule"/> list. First match wins in list order.
/// Used by the auto-switch engine in MainWindowViewModel and by the settings
/// page's "would fire" preview against its in-progress edit buffer.
/// </summary>
internal static class AppLayerRuleMatcher
{
    public static AppLayerRule? Match(ActiveWindowInfo? activeWindow, IEnumerable<AppLayerRule> rules)
    {
        var name = activeWindow?.ProcessName;
        if (string.IsNullOrWhiteSpace(name)) return null;
        foreach (var r in rules)
        {
            if (string.IsNullOrWhiteSpace(r.ProcessMatch)) continue;
            if (name.Contains(r.ProcessMatch, StringComparison.OrdinalIgnoreCase))
                return r;
        }
        return null;
    }
}
