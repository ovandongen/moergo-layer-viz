namespace MoergoLayerViz.Core.Settings;

/// <summary>
/// User-configurable settings, persisted as JSON.
/// </summary>
public record UserSettings
{
    /// <summary>
    /// Bumped on any breaking schema change (rename, type change, removed
    /// required field). <see cref="Settings.SettingsService.Load"/> uses this
    /// to dispatch migrations and to refuse files written by a newer version
    /// rather than silently nuking them.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the persisted settings file. Defaults to current for new files.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>
    /// Which Moergo keyboard profile to use. "GO60" or "Glove80". Chosen once by the user
    /// at first launch (or via settings) — remembered across sessions.
    /// </summary>
    public string Keyboard { get; init; } = "GO60";

    /// <summary>
    /// Last-used Moergo layout-editor JSON path per keyboard profile.
    /// Key = profile id ("GO60", "Glove80"), Value = absolute path. Empty until
    /// the user has picked a file — switching keyboards reloads whichever JSON
    /// was last associated with the new profile (or leaves the board blank if
    /// none yet).
    /// </summary>
    public Dictionary<string, string> LayoutJsonPaths { get; init; } = new();

    /// <summary>
    /// Per-layer color overrides, scoped per keyboard profile.
    /// Outer key = profile id, inner key = layer index, value = hex "#RRGGBB".
    /// </summary>
    public Dictionary<string, Dictionary<int, string>> LayerColors { get; init; } = new();

    /// <summary>Whether the window stays on top of other windows.</summary>
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>
    /// When true, the system tray icon is tinted to match the active layer's
    /// color (from <see cref="LayerColors"/>, falling back to the default
    /// palette). When false, the icon shows the original app artwork.
    /// </summary>
    public bool ColorTrayIconByActiveLayer { get; init; } = true;

    /// <summary>
    /// Show/hide global hotkey, neutral key name ("F12", "F18"). Registered
    /// via the platform's native hotkey API (Carbon on macOS, User32 on
    /// Windows). Inert on Linux.
    /// </summary>
    public string HotkeyKey { get; init; } = "F12";

    /// <summary>
    /// Modifier expression for the show/hide hotkey: "None" / empty, or one
    /// or more of "Ctrl", "Alt", "Shift", "Meta" joined with "+". Today the
    /// UI only exposes the key picker; this is reserved for future use.
    /// </summary>
    public string HotkeyModifiers { get; init; } = "None";

    /// <summary>Whether live key highlighting (pressed-key visualization) is enabled.</summary>
    public bool LiveKeyHighlighting { get; init; } = true;

    /// <summary>Whether to auto-switch the displayed layer when signal-macro keys are detected.</summary>
    public bool AutoLayerSwitch { get; init; } = true;

    /// <summary>Background solidity behind the board (0.0 = transparent, 1.0 = solid dark). Default: 0.5.</summary>
    public double BackgroundOpacity { get; init; } = 0.5;

    /// <summary>
    /// Color used for the press-highlight dot pulsed on each pressed key.
    /// Stored as "#RRGGBB". Default preserves the original saturated yellow.
    /// </summary>
    public string PressHighlightColor { get; init; } = "#FFD60A";

    /// <summary>Whether the user has seen the help/welcome window. Controls first-launch auto-open.</summary>
    public bool HasSeenHelp { get; init; } = false;

    /// <summary>UI language code (e.g. "en", "nl"). Default follows system locale, falls back to English.</summary>
    public string Language { get; init; } = "en";

    /// <summary>
    /// Rendering mode override. "auto" (default) uses the platform default GPU pipeline.
    /// "software" forces software rendering (workaround for GPU driver issues).
    /// The MOERGO_RENDER_MODE environment variable takes precedence over this setting.
    /// </summary>
    public string RenderingMode { get; init; } = "auto";

    /// <summary>Last window X position (pixels). Null = first launch, center on screen.</summary>
    public double? WindowX { get; init; }

    /// <summary>Last window Y position (pixels). Null = first launch, center on screen.</summary>
    public double? WindowY { get; init; }

    /// <summary>Last window width (pixels). Null = use default (1200).</summary>
    public double? WindowWidth { get; init; }

    /// <summary>Last window height (pixels). Null = use default (600).</summary>
    public double? WindowHeight { get; init; }

    /// <summary>Minimum log level for diagnostic logging. Default "Info". Values: Trace, Debug, Info, Warn, Error.</summary>
    public string LogLevel { get; init; } = "Info";

    /// <summary>
    /// When true, the two halves of the keyboard render stacked vertically
    /// instead of side-by-side. Useful for narrow / portrait window placements.
    /// </summary>
    public bool StackedLayout { get; init; } = false;

    /// <summary>Which half goes on top in stacked mode. "Left" or "Right".</summary>
    public string StackedTopHand { get; init; } = "Left";

    /// <summary>
    /// Modifier-key glyph style. "Mac" (default) shows ⌘ ⌥ ⌃ ⇪; "Windows"
    /// shows ⊞ Alt Ctrl ⇧. User-controlled, independent of the host OS so a
    /// macOS user looking at a coworker's Windows-typed keymap gets the
    /// expected glyphs and vice versa.
    /// </summary>
    public string ModifierStyle { get; init; } = "Mac";

    /// <summary>
    /// Per-keyboard app→layer rules. Outer key = profile id, value = ordered
    /// list of <see cref="AppLayerRule"/>; the first match (case-insensitive
    /// substring of ProcessName) wins.
    /// </summary>
    public Dictionary<string, List<AppLayerRule>> AppLayerRules { get; init; } = new();

    /// <summary>
    /// Master on/off toggle for firing <see cref="AppLayerRules"/> on focus
    /// change. Default off: rules persist but do nothing until the user opts
    /// in. Also gates the <c>IActiveWindowMonitor</c> lifetime — when off (or
    /// when no rules exist for the active keyboard), the monitor is stopped.
    /// </summary>
    public bool AutoSwitchKeyboardLayer { get; init; } = false;

    /// <summary>
    /// Per-keyboard fallback target when focus moves away from any matched
    /// app. Outer key = profile id, value = the fallback mode. Missing
    /// entries resolve to <see cref="AutoSwitchFallbackMode.Base"/>.
    /// </summary>
    public Dictionary<string, AutoSwitchFallbackMode> AutoSwitchFallback { get; init; } = new();

    /// <summary>
    /// Per-keyboard "escape hatch" key for the auto-switch engine: a single
    /// firmware key index (matrix position) that, when double-tapped within
    /// the submodule <c>MultiTapDetector</c>'s window, toggles the engine
    /// between the rule-driven layer and the fallback target. Missing means
    /// no exit key is configured for that keyboard — the firmware key events
    /// are ignored. The index matches
    /// <see cref="MoergoLayerViz.Core.Layout.KeyPosition.Index"/> which the
    /// firmware reports verbatim, so no translation is needed.
    /// </summary>
    /// <remarks>
    /// Pick a position whose binding doesn't emit OS keystrokes (e.g.
    /// Moergo's <c>&amp;magic</c>) so the firmware key reports flow through
    /// but no spurious text reaches the focused window.
    /// </remarks>
    public Dictionary<string, int> AutoSwitchExitKey { get; init; } = new();

    /// <summary>
    /// Per-keyboard opt-in: when true, a keypress whose binding on the
    /// active layer is literally <c>&amp;trans</c> triggers an early exit
    /// of an in-flight managed-app push (same fallback target as the
    /// double-tap exit key). Missing entries resolve to false. Independent
    /// from <see cref="AutoSwitchExitOnEmpty"/> — users can opt in to one,
    /// both, or neither.
    /// </summary>
    public Dictionary<string, bool> AutoSwitchExitOnTransparent { get; init; } = new();

    /// <summary>
    /// Per-keyboard opt-in: when true, a keypress whose binding on the
    /// active layer is literally <c>&amp;none</c> triggers an early exit
    /// of an in-flight managed-app push. See
    /// <see cref="AutoSwitchExitOnTransparent"/> for sibling semantics.
    /// </summary>
    public Dictionary<string, bool> AutoSwitchExitOnEmpty { get; init; } = new();

    /// <summary>
    /// Per-keyboard mouse-movement layer-push configuration. Outer key =
    /// profile id, value = the settings for that profile. Missing entries
    /// resolve to a disabled default (<see cref="MouseLayerSettings"/> with
    /// defaults). When enabled and HID-connected, mouse movement pushes
    /// <see cref="MouseLayerSettings.MouseLayerIndex"/>; idleness reverts to
    /// the configured target.
    /// </summary>
    public Dictionary<string, MouseLayerSettings> MouseLayer { get; init; } = new();
}
