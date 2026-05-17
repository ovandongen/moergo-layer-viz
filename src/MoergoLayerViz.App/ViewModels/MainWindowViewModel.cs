using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Input;
using MoergoLayerViz.Core.Keymap;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Models;
using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;
using ZmkHidProtocol.Protocol;

namespace MoergoLayerViz.App.ViewModels;

/// <summary>
/// Top-level view model for <c>MainWindow</c>. Owns the loaded keymap, the
/// active layer, the live-key tracker, and persists the user's choice of
/// keyboard + last-loaded JSON path.
/// </summary>
public partial class MainWindowViewModel : ObservableObject, IBoardSurface
{
    /// <summary>
    /// Always null on the main VM — the main window's BoardView is read-only.
    /// The picker VM (<see cref="ExitKeyPickerViewModel"/>) provides a real
    /// handler so taps register selections.
    /// </summary>
    public Action<int>? OnKeyTapped => null;

    private readonly ISettingsService _settingsService;

    private IKeyboardProfile _profile;
    private KeyboardConfig? _config;
    private string? _loadedLayoutPath;
    private string? _lastLoadError;
    // Last successful load's status text. Null when the current StatusMessage
    // is something else (error, transient, etc).
    private string? _loadStatusBase;

    // Resolves &trans fall-through via a precomputed predecessor graph.
    // Rebuilt on every layout load / profile switch; null when no layout
    // is loaded (callers treat that as "every binding is Transparent").
    private LayerBindingResolver? _bindingResolver;

    private readonly IHidPipeline _hid;
    private readonly ILayerPushCoordinator _push;

    // Press-highlight pipeline: per-layer (mod-set + keycode) → KeyViewModel
    // lookup, held-modifier set, modifier-grace deferral, per-key pulse.
    // Built lazily once the Keys collection is populated.
    private IKeyHighlightTracker? _highlightTracker;

    // --- UI-bindable state ---
    /// <summary>
    /// Transient/changing status text shown in the center of the top bar
    /// ("Switched to GO60", "Load error: …"). The persistent loaded-layout
    /// info lives in <see cref="LoadInfoTooltip"/>, surfaced via the
    /// InfoButton's hover tooltip.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// Hover tooltip for the InfoButton: persistent "Loaded foo.json (N layers) · keyboard hint",
    /// or "No layout loaded · keyboard hint" when nothing is loaded.
    /// </summary>
    public string LoadInfoTooltip =>
        $"{_loadStatusBase ?? Loc.Instance["Status_NoLayoutLoaded"]}  ·  {KeyboardStatusHint}";

    // Single write site for _loadStatusBase so the InfoButton tooltip refreshes
    // whenever the persistent loaded-info changes.
    private void SetLoadStatusBase(string? value)
    {
        _loadStatusBase = value;
        OnPropertyChanged(nameof(LoadInfoTooltip));
    }

    /// <summary>
    /// Suffix appended to the keyboard status hint describing the HID source
    /// state ("via Raw HID (Go60 Left)"). Empty until the coordinator has a
    /// connected source.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardStatusHint))]
    [NotifyPropertyChangedFor(nameof(LoadInfoTooltip))]
    private string _layerSourceHint = "";

    /// <summary>True while the HID source is connected.</summary>
    [ObservableProperty] private bool _isHidSourceActive;

    /// <summary>App-rules settings tab: hidden on Linux (no active-window API) and when HID is disconnected.</summary>
    public bool IsAppRulesTabVisible => !OperatingSystem.IsLinux() && IsHidSourceActive;

    partial void OnIsHidSourceActiveChanged(bool value) => OnPropertyChanged(nameof(IsAppRulesTabVisible));

    [ObservableProperty] private bool _isAlwaysOnTop;
    [ObservableProperty] private bool _colorTrayIconByActiveLayer;
    [ObservableProperty] private bool _isLiveHighlightingEnabled;
    [ObservableProperty] private bool _isAutoLayerSwitchEnabled;
    [ObservableProperty] private bool _hasLayoutLoaded;
    [ObservableProperty] private string _toastMessage = "";
    [ObservableProperty] private bool _isToastVisible;

    // Cancels the auto-dismiss timer on a re-shown toast or manual dismiss.
    private CancellationTokenSource? _toastCts;
    private const int ToastDurationMs = 4000;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActiveLayerTintColor))]
    private int _activeLayerIndex;

    /// <summary>Palette color for the active layer — used as the press-highlight pulse fill.</summary>
    public string ActiveLayerTintColor => LayerColorPalette.GetColor(_profile.Id, ActiveLayerIndex);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BoardBackground))]
    [NotifyPropertyChangedFor(nameof(TabBackground))]
    private double _backgroundOpacity;

    // Emitted in CSS-style #RRGGBBAA so HexColorToBrushConverter swaps the
    // alpha to the front for Avalonia's #AARRGGBB. The base color matches
    // svalboard's port — kept identical so a fully-solid slider lands on the
    // same dark plum the original UI used.
    public string BoardBackground => $"{AppTheme.BgCrustHex}{(int)(BackgroundOpacity * 255):X2}";

    // Tabs always retain at least 40% alpha so their text stays readable
    // even at slider 0; svalboard's formula, ported verbatim.
    public string TabBackground
    {
        get
        {
            const int baseAlpha = 0x66;
            var alpha = Math.Min(255, baseAlpha + (int)(BackgroundOpacity * (255 - baseAlpha)));
            return $"{AppTheme.BgCrustHex}{alpha:X2}";
        }
    }

    partial void OnBackgroundOpacityChanged(double value) =>
        PersistSetting(s => s with { BackgroundOpacity = value });

    partial void OnColorTrayIconByActiveLayerChanged(bool value) =>
        PersistSetting(s => s with { ColorTrayIconByActiveLayer = value });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PressHighlightStrokeColor))]
    private string _pressHighlightColor = AppTheme.PressHighlightDefaultHex;

    /// <summary>
    /// Rim color for the press dot — darkened version of <see cref="PressHighlightColor"/>
    /// so the dot reads against light layer fills. Multiplies each channel by 0.55
    /// to roughly mimic the original yellow→olive pairing in <see cref="AppTheme"/>.
    /// </summary>
    public string PressHighlightStrokeColor
    {
        get
        {
            if (MoergoLayerViz.Core.Colors.HexRgb.TryParse(PressHighlightColor, out var r, out var g, out var b))
                return $"#{(int)(r * 0.55):X2}{(int)(g * 0.55):X2}{(int)(b * 0.55):X2}";
            return AppTheme.PressHighlightStrokeFallbackHex;
        }
    }

    partial void OnPressHighlightColorChanged(string value) =>
        PersistSetting(s => s with { PressHighlightColor = value });

    /// <summary>
    /// Global show/hide hotkey key name (e.g. "F12"). Modifier handling
    /// lives in <see cref="UserSettings.HotkeyModifiers"/> and isn't
    /// user-editable today. Changing this raises <see cref="HotkeyKeyChanged"/>
    /// so the live <see cref="Services.IGlobalHotkeyService"/> rewires
    /// without restart.
    /// </summary>
    [ObservableProperty]
    private string _hotkeyKey = "F12";

    partial void OnHotkeyKeyChanged(string value)
    {
        PersistSetting(s => s with { HotkeyKey = value });
        HotkeyKeyChanged?.Invoke(value);
    }

    public event Action<string>? HotkeyKeyChanged;

    /// <summary>Read-through to the persisted modifier name. Not user-editable today; keeps the hotkey-rewire call site in App self-contained.</summary>
    public string HotkeyModifiers => _settingsService.Load().HotkeyModifiers;

    /// <summary>
    /// Sets or clears a per-layer color override for the currently active
    /// keyboard profile. Persists to <see cref="UserSettings.LayerColors"/>,
    /// updates the static palette, and refreshes both the layer-tab swatches
    /// (in place) and every key fill on the board.
    /// </summary>
    public void SetLayerColorOverride(int layerIndex, string? hex)
    {
        var profileId = _profile.Id;
        LayerColorPalette.SetOverride(profileId, layerIndex, hex);

        PersistSetting(s =>
        {
            var clone = new Dictionary<string, Dictionary<int, string>>();
            foreach (var (pid, perLayer) in s.LayerColors)
                clone[pid] = new Dictionary<int, string>(perLayer);

            if (string.IsNullOrWhiteSpace(hex))
            {
                if (clone.TryGetValue(profileId, out var inner))
                {
                    inner.Remove(layerIndex);
                    if (inner.Count == 0) clone.Remove(profileId);
                }
            }
            else
            {
                if (!clone.TryGetValue(profileId, out var inner))
                    clone[profileId] = inner = new Dictionary<int, string>();
                inner[layerIndex] = hex!;
            }
            return s with { LayerColors = clone };
        });

        // Repaint tab swatches in place (re-creating Layers would steal selection focus).
        foreach (var layer in Layers)
            layer.TabColor = LayerColorPalette.GetColor(profileId, layer.Index);
        // Re-resolve every key's fill — &lt / &mo keys reference arbitrary
        // layer colors, so changing layer 2's tint repaints layer 0's view too.
        if (_config is not null)
            ApplyActiveLayer(ActiveLayerIndex);
        OnPropertyChanged(nameof(ActiveLayerTintColor));
    }

    public ObservableCollection<KeyViewModel> Keys { get; } = new();
    /// <summary>Left-hand subset of <see cref="Keys"/>. Bound separately so the stacked-layout renderer can translate the half independently.</summary>
    public ObservableCollection<KeyViewModel> LeftKeys { get; } = new();
    /// <summary>Right-hand subset of <see cref="Keys"/>. Bound separately so the stacked-layout renderer can translate the half independently.</summary>
    public ObservableCollection<KeyViewModel> RightKeys { get; } = new();
    public ObservableCollection<LayerViewModel> Layers { get; } = new();

    // Per-profile bounding boxes for each hand, recomputed on profile change.
    // Used to translate each half's container in stacked mode so the bounding
    // box starts at the canvas-edge margin.
    private (double MinX, double MinY, double MaxX, double MaxY) _leftBounds;
    private (double MinX, double MinY, double MaxX, double MaxY) _rightBounds;

    /// <summary>Margin around the bounding boxes in stacked mode.</summary>
    private const double StackedMargin = 30;
    /// <summary>Vertical gap between the two halves in stacked mode.</summary>
    private const double StackedGap = 60;

    /// <summary>When true, the two halves render stacked vertically instead of side-by-side. Persisted across launches.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanvasWidth))]
    [NotifyPropertyChangedFor(nameof(CanvasHeight))]
    [NotifyPropertyChangedFor(nameof(LeftHandX))]
    [NotifyPropertyChangedFor(nameof(LeftHandY))]
    [NotifyPropertyChangedFor(nameof(RightHandX))]
    [NotifyPropertyChangedFor(nameof(RightHandY))]
    private bool _isStackedLayout;

    partial void OnIsStackedLayoutChanged(bool value) =>
        PersistSetting(s => s with { StackedLayout = value });

    /// <summary>Which half ("Left"/"Right") sits on top in stacked mode. Ignored in horizontal mode.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftHandX))]
    [NotifyPropertyChangedFor(nameof(LeftHandY))]
    [NotifyPropertyChangedFor(nameof(RightHandX))]
    [NotifyPropertyChangedFor(nameof(RightHandY))]
    private string _stackedTopHand = "Left";

    partial void OnStackedTopHandChanged(string value) =>
        PersistSetting(s => s with { StackedTopHand = value });

    /// <summary>
    /// True when the board renders Windows-style modifier glyphs (⊞ Alt Ctrl ⇧)
    /// instead of the default Mac set (⌘ ⌥ ⌃ ⇪). User-controlled via the
    /// toolbar; persisted across launches as <see cref="UserSettings.ModifierStyle"/>.
    /// Backed by the static <see cref="ZmkKeycodeLabel.CurrentModifierStyle"/>
    /// which every label-builder reads at render time.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModifierStyleIconData))]
    private bool _isWindowsModifierStyle;

    /// <summary>
    /// SVG path data for the toolbar button's icon: Apple silhouette when the
    /// Mac style is active, four-square Windows logo when the Windows style is
    /// active. Both paths are designed against a 24×24 viewBox so PathIcon
    /// scales them like the rest of the toolbar icons.
    /// </summary>
    public string ModifierStyleIconData => IsWindowsModifierStyle
        ? "M3 5.479L10.768 4.5v8.385H3V5.479zM11.232 4.5L21 3v9.885h-9.768V4.5zM3 13.115h7.768V21.5L3 20.521V13.115zM11.232 13.115H21V22.5l-9.768-1.385V13.115z"
        : "M17.05 20.28c-.98.95-2.05.8-3.08.35-1.09-.46-2.09-.48-3.24 0-1.44.62-2.2.44-3.06-.35C2.79 15.25 3.51 7.59 9.05 7.31c1.35.07 2.29.74 3.08.8 1.18-.24 2.31-.93 3.57-.84 1.51.12 2.65.72 3.4 1.8-3.12 1.87-2.38 5.98.48 7.13-.57 1.5-1.31 2.99-2.54 4.09zM12.03 7.25c-.15-2.23 1.66-4.07 3.74-4.25.29 2.58-2.34 4.5-3.74 4.25z";

    partial void OnIsWindowsModifierStyleChanged(bool value)
    {
        ZmkKeycodeLabel.CurrentModifierStyle = value ? ModifierStyle.Windows : ModifierStyle.Mac;
        PersistSetting(s => s with { ModifierStyle = value ? "Windows" : "Mac" });
        // Same redraw hook SetLayerColorOverride uses: rebuilds every key's
        // labels against the freshly-set static.
        if (_config is not null) ApplyActiveLayer(ActiveLayerIndex);
    }

    [RelayCommand]
    private void ToggleWindowsModifierStyle() => IsWindowsModifierStyle = !IsWindowsModifierStyle;

    /// <summary>Canvas size for the current keyboard profile and layout mode (drives BoardView's Canvas width/height).</summary>
    public double CanvasWidth => IsStackedLayout
        ? Math.Max(_leftBounds.MaxX - _leftBounds.MinX, _rightBounds.MaxX - _rightBounds.MinX) + 2 * StackedMargin
        : _profile.CanvasWidth;

    public double CanvasHeight => IsStackedLayout
        ? (_leftBounds.MaxY - _leftBounds.MinY) + (_rightBounds.MaxY - _rightBounds.MinY) + StackedGap + 2 * StackedMargin
        : _profile.CanvasHeight;

    /// <summary>
    /// Per-hand drawing surface size, independent of layout mode. Each per-hand
    /// ItemsControl in BoardView binds Width/Height to these so it has a
    /// non-zero render box — keys position themselves absolutely within it
    /// using the original profile coordinates, and the surrounding Canvas.Left/
    /// Top translates the whole surface for stacked mode.
    /// </summary>
    public double BoardSurfaceWidth => _profile.CanvasWidth;
    public double BoardSurfaceHeight => _profile.CanvasHeight;

    /// <summary>X translation applied to the left-hand container.</summary>
    public double LeftHandX => IsStackedLayout ? StackedMargin - _leftBounds.MinX : 0;

    /// <summary>Y translation applied to the left-hand container. Goes below the right hand if "Right" is on top.</summary>
    public double LeftHandY => !IsStackedLayout
        ? 0
        : (StackedTopHand == "Right"
            ? StackedMargin + (_rightBounds.MaxY - _rightBounds.MinY) + StackedGap - _leftBounds.MinY
            : StackedMargin - _leftBounds.MinY);

    /// <summary>X translation applied to the right-hand container.</summary>
    public double RightHandX => IsStackedLayout ? StackedMargin - _rightBounds.MinX : 0;

    /// <summary>Y translation applied to the right-hand container. Goes below the left hand by default.</summary>
    public double RightHandY => !IsStackedLayout
        ? 0
        : (StackedTopHand == "Right"
            ? StackedMargin - _rightBounds.MinY
            : StackedMargin + (_leftBounds.MaxY - _leftBounds.MinY) + StackedGap - _rightBounds.MinY);

    [RelayCommand]
    private void ToggleStackedLayout() => IsStackedLayout = !IsStackedLayout;

    /// <summary>All keyboard profiles the user can switch between, for the picker flyout.</summary>
    public IReadOnlyList<IKeyboardProfile> AvailableKeyboards => KeyboardProfileRegistry.All;

    /// <summary>
    /// Currently selected profile. Bound to the picker button label and drives
    /// checkmark selection in the flyout.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardStatusHint))]
    [NotifyPropertyChangedFor(nameof(LoadInfoTooltip))]
    [NotifyPropertyChangedFor(nameof(ActiveLayerTintColor))]
    private IKeyboardProfile _selectedKeyboard = null!;

    /// <summary>Right-aligned status-bar hint: "DisplayName · N keys" with layer-source suffix when known.</summary>
    public string KeyboardStatusHint
    {
        get
        {
            var core = Loc.Instance.Format("Status_KeyboardHintFormat",
                SelectedKeyboard?.DisplayName ?? "", SelectedKeyboard?.KeyCount ?? 0);
            return string.IsNullOrEmpty(LayerSourceHint) ? core : $"{core}  ·  {LayerSourceHint}";
        }
    }

    // --- Callbacks set by App.axaml.cs to bridge to the Window ---
    public Action? QuitRequested { get; set; }
    public Action? ShowWindowRequested { get; set; }
    public Action? ToggleWindowRequested { get; set; }
    public Func<Task>? LoadLayoutRequested { get; set; }
    public Func<Task>? CopyDiagnosticsRequested { get; set; }
    public Action? OpenSettingsRequested { get; set; }

    /// <summary>Path of the layout JSON the user currently has loaded, or null if none.</summary>
    public string? LoadedLayoutPath => _loadedLayoutPath;

    // --- Commands ---
    public IRelayCommand QuitCommand { get; }
    public IRelayCommand ShowCommand { get; }
    public IRelayCommand LoadLayoutCommand { get; }
    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand TogglePinCommand { get; }
    public IRelayCommand ToggleLiveHighlightingCommand { get; }
    public IRelayCommand OpenLogFolderCommand { get; }
    public IRelayCommand CopyDiagnosticsCommand { get; }
    public IRelayCommand<IKeyboardProfile> SelectKeyboardCommand { get; }
    public IRelayCommand DismissToastCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }

    /// <inheritdoc cref="AutoSwitchEngine.ExitTapKey"/>
    public int? ExitTapKey => _push.AutoSwitch.ExitTapKey;

    /// <inheritdoc cref="AutoSwitchEngine.SetExitTapKey"/>
    public void SetExitTapKey(int? index) => _push.AutoSwitch.SetExitTapKey(index);

    /// <inheritdoc cref="AutoSwitchEngine.ActiveWindow"/>
    public ActiveWindowInfo? ActiveWindow => _push.AutoSwitch.ActiveWindow;

    /// <inheritdoc cref="AutoSwitchEngine.AppLayerRules"/>
    public ObservableCollection<AppLayerRule> AppLayerRules => _push.AutoSwitch.AppLayerRules;

    /// <inheritdoc cref="AutoSwitchEngine.IsEnabled"/>
    public bool IsAutoSwitchKeyboardLayerEnabled
    {
        get => _push.AutoSwitch.IsEnabled;
        set => _push.AutoSwitch.IsEnabled = value;
    }

    /// <inheritdoc cref="AutoSwitchEngine.FallbackMode"/>
    public AutoSwitchFallbackMode AutoSwitchFallbackMode
    {
        get => _push.AutoSwitch.FallbackMode;
        set => _push.AutoSwitch.FallbackMode = value;
    }

    /// <inheritdoc cref="AutoSwitchEngine.ExitOnTransparentKey"/>
    public bool AutoSwitchExitOnTransparentKey
    {
        get => _push.AutoSwitch.ExitOnTransparentKey;
        set => _push.AutoSwitch.ExitOnTransparentKey = value;
    }

    /// <inheritdoc cref="AutoSwitchEngine.ExitOnEmptyKey"/>
    public bool AutoSwitchExitOnEmptyKey
    {
        get => _push.AutoSwitch.ExitOnEmptyKey;
        set => _push.AutoSwitch.ExitOnEmptyKey = value;
    }

    /// <inheritdoc cref="AutoSwitchEngine.MatchedAppLayerRule"/>
    public AppLayerRule? MatchedAppLayerRule => _push.AutoSwitch.MatchedAppLayerRule;

    /// <inheritdoc cref="AutoSwitchEngine.ApplyAppLayerRules"/>
    public void ApplyAppLayerRules(IReadOnlyList<AppLayerRule> rules) =>
        _push.AutoSwitch.ApplyAppLayerRules(rules);

    /// <summary>
    /// Returns the active profile's mouse-layer settings. Falls back to a
    /// fresh disabled default when no engine is wired up (tests) or no
    /// override has been persisted for the active profile yet.
    /// </summary>
    public MouseLayerSettings GetActiveMouseLayerSettings()
    {
        if (_push.MouseLayer is not null) return _push.MouseLayer.CurrentSettings;
        var s = _settingsService.Load();
        return s.MouseLayer.TryGetValue(_profile.Id, out var loaded)
            ? loaded
            : new MouseLayerSettings();
    }

    /// <summary>
    /// Persists the active profile's mouse-layer settings and re-arms the
    /// engine (idle timeout + enabled-state reconciliation).
    /// </summary>
    public void ApplyMouseLayerSettings(MouseLayerSettings settings) =>
        _push.MouseLayer?.ApplySettings(settings);

    public MainWindowViewModel(
        ISettingsService settingsService,
        IActiveWindowMonitor? activeWindowMonitor = null,
        IMouseIdleMonitor? mouseIdleMonitor = null)
    {
        _settingsService = settingsService;
        var s = settingsService.Load();
        _profile = KeyboardProfileRegistry.TryResolve(s.Keyboard, out var p) ? p : new Go60Profile();
        _selectedKeyboard = _profile;
        _isAlwaysOnTop = s.AlwaysOnTop;
        _colorTrayIconByActiveLayer = s.ColorTrayIconByActiveLayer;
        _isLiveHighlightingEnabled = s.LiveKeyHighlighting;
        _isAutoLayerSwitchEnabled = s.AutoLayerSwitch;
        _backgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0.0, 1.0);
        if (!string.IsNullOrWhiteSpace(s.PressHighlightColor))
            _pressHighlightColor = s.PressHighlightColor;
        if (!string.IsNullOrWhiteSpace(s.HotkeyKey))
            _hotkeyKey = s.HotkeyKey;
        _isStackedLayout = s.StackedLayout;
        _stackedTopHand = string.IsNullOrWhiteSpace(s.StackedTopHand) ? "Left" : s.StackedTopHand;
        _isWindowsModifierStyle = string.Equals(s.ModifierStyle, "Windows", StringComparison.OrdinalIgnoreCase);
        // Seed the static *before* the first ApplyActiveLayer so initial render
        // already uses the persisted glyph set (the partial change handler is
        // not invoked when the backing field is assigned directly).
        ZmkKeycodeLabel.CurrentModifierStyle = _isWindowsModifierStyle ? ModifierStyle.Windows : ModifierStyle.Mac;
        // Seed the static palette with persisted per-keyboard, per-layer overrides
        // so the very first paint already reflects the user's customization.
        LayerColorPalette.SetOverrides(s.LayerColors);

        // HID stack owns the layer source, command sender, and state tracker.
        // Constructed eagerly so push surfaces are available before Start();
        // the underlying RawHidLayerSource doesn't open the device until
        // Start() runs (triggered by ToggleLiveHighlighting).
        _hid = new HidPipeline(_profile);
        _hid.ActiveLayerChanged += OnActiveLayerChanged;
        _hid.ConnectionChanged += OnActiveSourceChanged;

        // Push coordinator owns AutoSwitch + MouseLayer and the handoff between
        // them. Routes both engines' pushes through _hid; redirects AutoSwitch
        // pushes into MouseLayer's revert target while a mouse push is in
        // flight. Forwards HID key-position events into AutoSwitch's exit-tap
        // detector and re-raises them for the highlight tracker below.
        _push = new LayerPushCoordinator(
            _hid,
            getRenderedLayer: () => ActiveLayerIndex,
            _profile,
            settingsService,
            activeWindowMonitor,
            mouseIdleMonitor);
        _push.KeyPositionForUi += OnKeyPositionForHighlight;
        // Transparent-key exit predicate reads the currently-loaded config at
        // call time, so swapping layouts or profiles needs no re-binding.
        _push.SetTransparencyPredicate(ClassifyBindingOnLayer);
        _push.AutoSwitch.PropertyChanged += (_, e) =>
        {
            var relay = e.PropertyName switch
            {
                nameof(AutoSwitchEngine.IsEnabled) => nameof(IsAutoSwitchKeyboardLayerEnabled),
                nameof(AutoSwitchEngine.FallbackMode) => nameof(AutoSwitchFallbackMode),
                nameof(AutoSwitchEngine.ActiveWindow) => nameof(ActiveWindow),
                nameof(AutoSwitchEngine.MatchedAppLayerRule) => nameof(MatchedAppLayerRule),
                nameof(AutoSwitchEngine.ExitTapKey) => nameof(ExitTapKey),
                nameof(AutoSwitchEngine.ExitOnTransparentKey) => nameof(AutoSwitchExitOnTransparentKey),
                nameof(AutoSwitchEngine.ExitOnEmptyKey) => nameof(AutoSwitchExitOnEmptyKey),
                _ => null,
            };
            if (relay is not null) OnPropertyChanged(relay);
        };

        QuitCommand = new RelayCommand(() => QuitRequested?.Invoke());
        ShowCommand = new RelayCommand(() => ShowWindowRequested?.Invoke());
        LoadLayoutCommand = new AsyncRelayCommand(async () =>
        {
            if (LoadLayoutRequested is not null) await LoadLayoutRequested();
        });
        RefreshCommand = new RelayCommand(() =>
        {
            var paths = _settingsService.Load().LayoutJsonPaths;
            if (paths.TryGetValue(_profile.Id, out var path) && !string.IsNullOrEmpty(path))
                LoadLayoutFromPath(path);
        });
        TogglePinCommand = new RelayCommand(() =>
        {
            IsAlwaysOnTop = !IsAlwaysOnTop;
            PersistSetting(s2 => s2 with { AlwaysOnTop = IsAlwaysOnTop });
        });
        ToggleLiveHighlightingCommand = new RelayCommand(ToggleLiveHighlighting);
        OpenLogFolderCommand = new RelayCommand(() =>
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = DiagnosticLog.GetLogDirectory(),
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                SetLoadStatusBase(null);
                StatusMessage = $"Could not open log folder: {ex.Message}";
            }
        });
        CopyDiagnosticsCommand = new AsyncRelayCommand(async () =>
        {
            if (CopyDiagnosticsRequested is not null) await CopyDiagnosticsRequested();
        });
        SelectKeyboardCommand = new RelayCommand<IKeyboardProfile>(SelectKeyboard);
        DismissToastCommand = new RelayCommand(DismissToast);
        OpenSettingsCommand = new RelayCommand(() => OpenSettingsRequested?.Invoke());

        BuildKeysFromProfile();
    }

    /// <summary>
    /// Post-startup entry point: load last-used layout, spin up the key-event
    /// hook, and set an opening status message.
    /// </summary>
    public void InitializeAsync()
    {
        var s = _settingsService.Load();
        if (TryGetStoredPathForProfile(s, _profile.Id, out var storedPath))
        {
            LoadLayoutFromPath(storedPath);
        }
        else
        {
            SetLoadStatusBase(null);
            StatusMessage = Loc.Instance["Status_NoLayoutLoaded"];
        }

        if (IsLiveHighlightingEnabled)
            StartKeyEventTracking();
    }

    public void LoadLayoutFromPath(string path)
    {
        try
        {
            var config = MoergoJsonLoader.LoadFromFile(path);
            var bindingCount = config.LayerCount > 0 ? config.Layers[0].Bindings.Count : 0;
            var keyCountMismatch = config.LayerCount > 0 && bindingCount != _profile.KeyCount;
            IKeyboardProfile? autoSwitchedTo = null;
            IKeyboardProfile? unmatchedProfile = null;
            if (keyCountMismatch)
            {
                var matching = KeyboardProfileRegistry.All
                    .FirstOrDefault(p => p.KeyCount == bindingCount);
                if (matching is not null)
                {
                    unmatchedProfile = _profile;
                    _profile = matching;
                    SelectedKeyboard = matching;
                    BuildKeysFromProfile();
                    PersistSetting(s => s with { Keyboard = matching.Id });
                    // Re-scope HID discovery — see SelectKeyboard for context.
                    _hid.SetActiveProfile(matching);
                    autoSwitchedTo = matching;
                    DiagnosticLog.Info("MainVM",
                        $"Auto-switched profile to {matching.Id} ({bindingCount} keys) on load");
                }
                else
                {
                    DiagnosticLog.Warn("MainVM",
                        $"Loaded layout has {bindingCount} keys but no profile matches; staying on {_profile.Id}");
                }
            }

            _config = config;
            _bindingResolver = new LayerBindingResolver(config);

            RebuildLayers();
            ApplyActiveLayer(0);
            HasLayoutLoaded = true;
            _loadedLayoutPath = path;
            _lastLoadError = null;

            PersistSetting(s =>
            {
                var paths = new Dictionary<string, string>(s.LayoutJsonPaths) { [_profile.Id] = path };
                return s with { LayoutJsonPaths = paths };
            });

            var baseMsg = Loc.Instance.Format("Status_Loaded",
                Path.GetFileName(path), config.LayerCount);
            if (autoSwitchedTo is not null)
            {
                baseMsg += " — " + Loc.Instance.Format("Status_AutoSwitchedKeyboard",
                    autoSwitchedTo.DisplayName);
            }
            else if (keyCountMismatch)
            {
                baseMsg += " — " + Loc.Instance.Format("Status_LoadKeyCountMismatch",
                    bindingCount, _profile.DisplayName, _profile.KeyCount);
            }
            SetLoadStatusBase(baseMsg);
            StatusMessage = baseMsg;
            DiagnosticLog.Info("MainVM", $"Loaded '{path}'");
        }
        catch (Exception ex)
        {
            SetLoadStatusBase(null);
            StatusMessage = Loc.Instance.Format("Status_LoadErrorFormat", ex.Message);
            _lastLoadError = $"{path}: {ex.GetType().Name}: {ex.Message}";
            DiagnosticLog.Error("MainVM", $"Load failed: {ex}");
            ShowToast(Loc.Instance.Format("Toast_LoadFailedFormat", Path.GetFileName(path), ex.Message));
        }
    }

    /// <summary>
    /// Shows a transient toast banner that auto-dismisses after
    /// <see cref="ToastDurationMs"/>. Re-entry cancels the previous timer so
    /// the new message gets the full display window. Click on the toast
    /// dismisses early via <see cref="DismissToastCommand"/>.
    /// </summary>
    public void ShowToast(string message)
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        var cts = new CancellationTokenSource();
        _toastCts = cts;

        ToastMessage = message;
        IsToastVisible = true;

        _ = Task.Delay(ToastDurationMs, cts.Token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (_toastCts == cts)
                {
                    IsToastVisible = false;
                    _toastCts = null;
                    cts.Dispose();
                }
            });
        }, TaskScheduler.Default);
    }

    private void DismissToast()
    {
        _toastCts?.Cancel();
        _toastCts?.Dispose();
        _toastCts = null;
        IsToastVisible = false;
    }

    /// <summary>
    /// Builds a snapshot of runtime state (active settings, loaded layout,
    /// last load error) for inclusion in
    /// <see cref="DiagnosticLog.CollectDiagnosticReport"/>.
    /// </summary>
    public string BuildDiagnosticsSnapshot()
    {
        var sb = new StringBuilder();
        sb.AppendLine("--- Active Settings ---");
        try
        {
            var settings = _settingsService.Load();
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            sb.AppendLine(json);
        }
        catch (Exception ex)
        {
            sb.AppendLine($"(could not serialize settings: {ex.Message})");
        }
        sb.AppendLine();

        sb.AppendLine("--- Active Keyboard ---");
        sb.AppendLine($"Profile: {_profile.DisplayName} ({_profile.Id}), {_profile.KeyCount} keys");
        sb.AppendLine($"Loaded layout: {_loadedLayoutPath ?? "(none)"}");
        sb.AppendLine($"Last load error: {_lastLoadError ?? "(none)"}");

        return sb.ToString();
    }

    /// <summary>Gracefully stops live tracking; idempotent. Called on window close / quit.</summary>
    public void Shutdown()
    {
        // If the mouse-layer engine is mid-push, revert *before* tearing down
        // HID so we don't leave the keyboard pinned to the mouse layer after
        // we're no longer in control. Synchronous + bounded so a stuck HID
        // write can't hang shutdown.
        _push.RevertMouseLayerForShutdown(TimeSpan.FromMilliseconds(500));
        StopKeyEventTracking();
        _push.Shutdown();
        _push.Dispose();
        _hid.Dispose();
    }

    // --- Internals ---

    private void BuildKeysFromProfile()
    {
        Keys.Clear();
        LeftKeys.Clear();
        RightKeys.Clear();

        foreach (var pos in _profile.Keys)
        {
            var vm = new KeyViewModel(pos);
            Keys.Add(vm);
            (pos.Hand == Hand.Left ? LeftKeys : RightKeys).Add(vm);
        }

        // Rotated bounds matter for boards like Glove80 whose shared-pivot thumb
        // cluster swings far outside the unrotated (X, Y, W, H) rect.
        _leftBounds = _profile.Keys.Where(k => k.Hand == Hand.Left).RotatedBounds();
        _rightBounds = _profile.Keys.Where(k => k.Hand == Hand.Right).RotatedBounds();

        // Layout-derived properties depend on the freshly-computed bounds.
        OnPropertyChanged(nameof(CanvasWidth));
        OnPropertyChanged(nameof(CanvasHeight));
        OnPropertyChanged(nameof(BoardSurfaceWidth));
        OnPropertyChanged(nameof(BoardSurfaceHeight));
        OnPropertyChanged(nameof(LeftHandX));
        OnPropertyChanged(nameof(LeftHandY));
        OnPropertyChanged(nameof(RightHandX));
        OnPropertyChanged(nameof(RightHandY));
    }

    private void SelectKeyboard(IKeyboardProfile? profile)
    {
        if (profile is null) return;
        if (string.Equals(profile.Id, _profile.Id, StringComparison.OrdinalIgnoreCase)) return;

        _profile = profile;
        SelectedKeyboard = profile;
        BuildKeysFromProfile();
        _push.SetActiveProfile(profile);

        var layoutFits = _config is not null
            && _config.LayerCount > 0
            && _config.Layers[0].Bindings.Count == profile.KeyCount;

        if (_config is not null && !layoutFits)
        {
            _config = null;
            _bindingResolver = null;
            Layers.Clear();
            ActiveLayerIndex = 0;
            HasLayoutLoaded = false;
            SetLoadStatusBase(null);
            StatusMessage = Loc.Instance.Format("Status_KeyboardSwitchedUnloaded", profile.DisplayName);
        }
        else if (_config is not null)
        {
            ApplyActiveLayer(ActiveLayerIndex);
            SetLoadStatusBase(null);
            StatusMessage = Loc.Instance.Format("Status_KeyboardSwitched", profile.DisplayName);
        }
        else
        {
            SetLoadStatusBase(null);
            StatusMessage = Loc.Instance.Format("Status_KeyboardSwitched", profile.DisplayName);
        }

        PersistSetting(s => s with { Keyboard = profile.Id });
        DiagnosticLog.Info("MainVM", $"Keyboard profile switched to {profile.Id}");

        // Re-scope HID discovery to the new profile so a Go60 stops feeding
        // reports into a Glove80 layout (or vice versa). No-op when HID is
        // disabled or the source isn't running.
        _hid.SetActiveProfile(profile);

        // Auto-load whichever JSON the user last associated with this keyboard.
        // If the previously-loaded layout already fits, leave it alone.
        if (!HasLayoutLoaded
            && TryGetStoredPathForProfile(_settingsService.Load(), profile.Id, out var storedPath))
        {
            LoadLayoutFromPath(storedPath);
        }
    }

    /// <summary>
    /// Returns the persisted JSON path for the given profile if one exists and
    /// the file is still on disk. If the entry points at a file that has gone
    /// missing, logs a warning and removes the stale entry from settings so
    /// startup doesn't keep complaining about it.
    /// </summary>
    private bool TryGetStoredPathForProfile(UserSettings s, string profileId, out string path)
    {
        path = "";
        if (!s.LayoutJsonPaths.TryGetValue(profileId, out var stored) || string.IsNullOrWhiteSpace(stored))
            return false;
        if (File.Exists(stored))
        {
            path = stored;
            return true;
        }

        DiagnosticLog.Warn("MainVM", $"Stored layout for {profileId} is missing on disk: {stored}");
        PersistSetting(curr =>
        {
            var paths = new Dictionary<string, string>(curr.LayoutJsonPaths);
            paths.Remove(profileId);
            return curr with { LayoutJsonPaths = paths };
        });
        return false;
    }

    private void RebuildLayers()
    {
        Layers.Clear();
        if (_config is null) return;
        foreach (var layer in _config.Layers)
        {
            Layers.Add(new LayerViewModel(
                layer.Index,
                $"{layer.Index} : {layer.Name}",
                LayerColorPalette.GetColor(_profile.Id, layer.Index),
                SelectLayer));
        }
    }

    private void SelectLayer(int index)
    {
        ApplyActiveLayer(index);
    }

    /// <summary>Routes a layer push through the HID pipeline. Test-tab plumbing for SettingsViewModel.</summary>
    public void PushLayerToKeyboard(int index) => _hid.PushLayer(index);

    /// <summary>
    /// Classifies the currently-loaded config's binding at
    /// (<paramref name="layer"/>, <paramref name="key"/>) as
    /// <see cref="TransparentBindingKind.Transparent"/> for <c>&amp;trans</c>,
    /// <see cref="TransparentBindingKind.Empty"/> for <c>&amp;none</c>, or
    /// null for anything else (including when no config is loaded or indices
    /// are out of range). No fall-through walk — exit fires on the visible
    /// binding only.
    /// </summary>
    private TransparentBindingKind? ClassifyBindingOnLayer(int layer, int key)
    {
        var cfg = _config;
        if (cfg is null) return null;
        if (layer < 0 || layer >= cfg.Layers.Count) return null;
        var bindings = cfg.Layers[layer].Bindings;
        if (key < 0 || key >= bindings.Count) return null;
        return bindings[key].Behavior switch
        {
            "&trans" => TransparentBindingKind.Transparent,
            "&none" => TransparentBindingKind.Empty,
            _ => null,
        };
    }

    /// <summary>
    /// Sends a 0xFD GetDeviceInfo request and awaits the 0xFE reply.
    /// Returns null when no HID is connected, on timeout, or on transport
    /// failure. Test-tab plumbing only.
    /// </summary>
    public Task<DeviceInfo?> QueryDeviceInfoAsync(TimeSpan timeout, CancellationToken ct) =>
        _hid.QueryDeviceInfoAsync(timeout, ct);

    /// <summary>
    /// Sends a 0xFB GetConfigId request and awaits the 0xFA reply.
    /// Returns null when no HID is connected, on timeout, or on transport
    /// failure. Empty string means the firmware has no
    /// CONFIG_HID_VIZ_CONFIG_ID set. Test-tab plumbing only.
    /// </summary>
    public Task<string?> QueryConfigIdAsync(TimeSpan timeout, CancellationToken ct) =>
        _hid.QueryConfigIdAsync(timeout, ct);

    private void ApplyActiveLayer(int index)
    {
        if (_config is null) return;
        if (index < 0 || index >= _config.Layers.Count) return;

        ActiveLayerIndex = index;
        var layer = _config.Layers[index];

        var holdTapByName = _config.HoldTaps.ToDictionary(h => h.Name, StringComparer.Ordinal);

        var combosByKey = new Dictionary<int, List<MoergoCombo>>();
        foreach (var combo in _config.Combos)
        {
            if (!combo.AppliesToLayer(layer.Index)) continue;
            foreach (var keyIdx in combo.KeyPositions)
            {
                if (!combosByKey.TryGetValue(keyIdx, out var list))
                    combosByKey[keyIdx] = list = new List<MoergoCombo>();
                list.Add(combo);
            }
        }

        for (int i = 0; i < Keys.Count; i++)
        {
            // Resolve the effective binding by walking the predecessor graph:
            // `&trans` falls through to the layer that can activate this one
            // (recursively) until a non-transparent binding is found.
            var binding = _bindingResolver?.ResolveEffectiveBinding(layer.Index, i) ?? KeyBinding.Transparent;

            holdTapByName.TryGetValue(binding.Behavior, out var holdTap);
            var targetLayer = LayerBindingResolver.ResolveTargetLayer(binding, holdTap);
            var targetLayerName = targetLayer is int tl && tl >= 0 && tl < _config.Layers.Count
                ? _config.Layers[tl].Name
                : null;
            Keys[i].ApplyBinding(
                binding,
                targetLayer: targetLayer,
                targetLayerName: targetLayerName,
                profileId: _profile.Id,
                holdTap: holdTap);
        }

        // Second pass — every key's label is now settled, so combo participants
        // can be named by their rendered label (Q + W) in the tooltip.
        string LabelLookup(int idx) => idx >= 0 && idx < Keys.Count ? Keys[idx].Label : "";
        for (int i = 0; i < Keys.Count; i++)
        {
            if (combosByKey.TryGetValue(i, out var keyCombos))
                Keys[i].SetCombos(keyCombos, LabelLookup);
        }

        for (int i = 0; i < Layers.Count; i++)
            Layers[i].IsSelected = Layers[i].Index == index;
    }

    /// <summary>
    /// Lazily-instantiated press-highlight tracker. Constructed on first use
    /// so it captures the <see cref="Keys"/> collection after
    /// <see cref="BuildKeysFromProfile"/> has populated it.
    /// </summary>
    private IKeyHighlightTracker HighlightTracker =>
        _highlightTracker ??= new KeyHighlightTracker(Keys);


    private void ToggleLiveHighlighting()
    {
        var enable = !IsLiveHighlightingEnabled;
        IsLiveHighlightingEnabled = enable;
        // Merged toolbar toggle: AutoLayerSwitch rides along with
        // LiveHighlighting so users get a single "live" master switch.
        // The underlying booleans stay distinct in UserSettings so any
        // future independent control point can still flip them apart.
        IsAutoLayerSwitchEnabled = enable;
        PersistSetting(s2 => s2 with
        {
            LiveKeyHighlighting = enable,
            AutoLayerSwitch = enable,
        });

        if (enable)
        {
            StartKeyEventTracking();
        }
        else
        {
            StopKeyEventTracking();
            ResetLayerState();
        }
    }

    private void StartKeyEventTracking()
    {
        _hid.Start();
        // Initial label sync — the source may already have settled the
        // active state before our subscription was attached above.
        OnActiveSourceChanged();
    }

    private void StopKeyEventTracking()
    {
        // Revert any in-flight mouse-layer push *before* tearing down HID.
        // Otherwise the engine's _preMoveLayer stays set across the stop,
        // and the next enable captures the (still-held) mouse layer as
        // the new pre-move layer — getting stuck pushing/reverting onto
        // the mouse layer.
        _push.RevertMouseLayerForShutdown(TimeSpan.FromMilliseconds(500));
        _hid.Stop();
        IsHidSourceActive = false;
        LayerSourceHint = "";
    }

    private void OnActiveLayerChanged(int layer)
    {
        if (!IsAutoLayerSwitchEnabled) return;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ApplyActiveLayer(layer));
    }

    private void OnActiveSourceChanged()
    {
        var hidActive = _hid.IsConnected;
        var label = _hid.SourceLabel;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsHidSourceActive = hidActive;
            LayerSourceHint = string.IsNullOrEmpty(label)
                ? ""
                : Loc.Instance.Format("Status_LayerSourceHintFormat", label);
            // Mouse-layer engine only fires when HID is connected; toggling
            // it stops the OS-level mouse tap while disconnected.
            _push.OnHidConnectionChanged();
        });
    }

    /// <summary>
    /// Press-highlight path for the HID source: the firmware reports the
    /// physical matrix position so we go straight to <see cref="Keys"/>[position].
    /// </summary>
    private void OnKeyPositionForHighlight(int position, bool pressed)
    {
        if (!pressed) return;
        HighlightTracker.PulseAt(position);
    }

    private void ResetLayerState()
    {
        ApplyActiveLayer(0);
    }

    private void PersistSetting(Func<UserSettings, UserSettings> update)
    {
        try
        {
            var s = _settingsService.Load();
            _settingsService.Save(update(s));
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("MainVM", $"Persist settings failed: {ex.Message}");
        }
    }
}
