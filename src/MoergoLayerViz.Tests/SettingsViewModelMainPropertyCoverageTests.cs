using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using MoergoLayerViz.App.ViewModels;
using Xunit;

namespace MoergoLayerViz.Tests;

/// <summary>
/// Guards <see cref="SettingsViewModel"/>'s dispatch table for re-raising
/// derived properties against silent drift. When someone adds a new public
/// property to <see cref="MainWindowViewModel"/>, they must either:
/// (a) add an entry in <c>_mainPropagators</c> to forward changes to a
/// derived SettingsVM property, or
/// (b) add the name to <see cref="IgnoredMainProperties"/> below, declaring
/// that the property is intentionally not surfaced through SettingsVM
/// (e.g. bound directly in XAML, a command, a callback, or a pure
/// derived/geometry value).
///
/// Either way, the choice is made deliberately instead of silently
/// breaking the settings view.
/// </summary>
public class SettingsViewModelMainPropertyCoverageTests
{
    /// <summary>
    /// Public MainWindowViewModel properties that intentionally do NOT
    /// propagate through SettingsViewModel. Each entry is one of:
    /// (a) bound directly to MainViewModel in SettingsWindow.axaml — no
    ///     transform needed, OneWay/TwoWay handles it,
    /// (b) not surfaced in settings at all (board geometry, callbacks,
    ///     commands, transient UI state like toasts),
    /// (c) a derived/computed property whose source is already covered
    ///     elsewhere in this set.
    /// </summary>
    private static readonly HashSet<string> IgnoredMainProperties = new()
    {
        // Bound directly in SettingsWindow.axaml — no SettingsVM transform needed.
        "HotkeyKey",
        "HotkeyModifiers",
        "IsStackedLayout",
        "StackedTopHand",
        "IsWindowsModifierStyle",
        "ColorTrayIconByActiveLayer",
        "IsLiveHighlightingEnabled",
        "IsAutoLayerSwitchEnabled",
        "IsAutoSwitchKeyboardLayerEnabled",
        "IsAlwaysOnTop",

        // Pure derived / geometry / display-only — not user-facing in settings.
        "LoadInfoTooltip",
        "ActiveLayerTintColor",
        "ActiveLayerIndex",
        "BoardBackground",
        "TabBackground",
        "PressHighlightStrokeColor",
        "ModifierStyleIconData",
        "CanvasWidth",
        "CanvasHeight",
        "BoardSurfaceWidth",
        "BoardSurfaceHeight",
        "LeftHandX",
        "LeftHandY",
        "RightHandX",
        "RightHandY",
        "KeyboardStatusHint",
        "IsAppRulesTabVisible",
        "LoadedLayoutPath",

        // Transient main-window UI state — settings doesn't render these.
        "StatusMessage",
        "ToastMessage",
        "IsToastVisible",
        "HasLayoutLoaded",
        "IsHidSourceActive",

        // Engine-derived state surfaced through MainVM but already covered
        // by the propagator entries that change WouldFire / app-rules
        // matching (ActiveWindow drives WouldFireText).
        "MatchedAppLayerRule",
    };

    [Fact]
    public void EveryPublicMainProperty_IsEitherPropagatedOrIgnored()
    {
        var propagated = SettingsViewModel.PropagatedMainPropertyNames;

        var missing = new List<string>();
        foreach (var prop in CandidateMainProperties())
        {
            if (propagated.Contains(prop.Name)) continue;
            if (IgnoredMainProperties.Contains(prop.Name)) continue;
            missing.Add(prop.Name);
        }

        Assert.True(missing.Count == 0,
            "MainWindowViewModel has public properties that are not covered by " +
            $"SettingsViewModel._mainPropagators or IgnoredMainProperties: " +
            $"[{string.Join(", ", missing)}]. " +
            "If the new property needs to drive a derived SettingsVM display, " +
            "add an entry to _mainPropagators. Otherwise add it to " +
            $"{nameof(IgnoredMainProperties)} in this test file.");
    }

    [Fact]
    public void IgnoredAndPropagated_AreDisjoint()
    {
        var propagated = SettingsViewModel.PropagatedMainPropertyNames;
        var overlap = propagated.Intersect(IgnoredMainProperties).ToList();
        Assert.True(overlap.Count == 0,
            $"Properties appear in both _mainPropagators and IgnoredMainProperties: " +
            $"[{string.Join(", ", overlap)}]. Pick one.");
    }

    [Fact]
    public void IgnoredEntries_AllNameRealProperties()
    {
        var names = CandidateMainProperties().Select(p => p.Name).ToHashSet();
        var stale = IgnoredMainProperties.Where(n => !names.Contains(n)).ToList();
        Assert.True(stale.Count == 0,
            $"IgnoredMainProperties references properties that no longer exist " +
            $"on MainWindowViewModel: [{string.Join(", ", stale)}]. Remove them.");
    }

    /// <summary>
    /// Returns public instance properties on <see cref="MainWindowViewModel"/>
    /// that a SettingsVM author would plausibly want to mirror. Filters out:
    /// commands (ICommand), callbacks (Action/Func), and non-string
    /// collections — those are wired by direct binding or not at all.
    /// </summary>
    private static IEnumerable<PropertyInfo> CandidateMainProperties()
    {
        foreach (var prop in typeof(MainWindowViewModel)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.GetGetMethod() is null) continue;
            var t = prop.PropertyType;

            // Skip commands.
            if (typeof(ICommand).IsAssignableFrom(t)) continue;

            // Skip callbacks (delegate-typed properties).
            if (typeof(System.Delegate).IsAssignableFrom(t)) continue;

            // Skip collections except string (string is IEnumerable<char>).
            if (t != typeof(string) && typeof(IEnumerable).IsAssignableFrom(t))
                continue;

            yield return prop;
        }
    }
}
