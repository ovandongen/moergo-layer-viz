using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Input;
using MoergoLayerViz.Core.Keymap;
using MoergoLayerViz.Core.Layout;
using MoergoLayerViz.Core.Models;
using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;
using ZmkHidProtocol.Building;
using ZmkHidProtocol.Transport;

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
    private IReadOnlyList<SignalMacro> _signalMacros = Array.Empty<SignalMacro>();
    // Auto + user-manual layer-signal mappings. The live tracker uses the
    // merged view; the keymap renderer uses the auto-only view so manual
    // mappings never affect how labels are drawn.
    private readonly MergedSignalTableManager _signalManager;
    private IReadOnlyList<UntrackableLayerSwitch> _untrackable = Array.Empty<UntrackableLayerSwitch>();
    private string? _loadedLayoutPath;
    private string? _lastLoadError;
    // Last successful load's status text *without* the dynamic untrackable
    // suffix. Kept so we can recompose StatusMessage when the active layer
    // source flips (HID makes the untrackable warning irrelevant). Null when
    // the current StatusMessage is something else (error, transient, etc).
    private string? _loadStatusBase;

    // Resolves &trans fall-through via a precomputed predecessor graph.
    // Rebuilt on every layout load / profile switch; null when no layout
    // is loaded (callers treat that as "every binding is Transparent").
    private LayerBindingResolver? _bindingResolver;

    private IKeyEventSource? _keyEventSource;
    private HotkeyLayerTracker? _tracker;
    private LayerSourceCoordinator? _layerCoordinator;
    private CommandSender? _commandSender;
    private LayerStateTracker? _layerStateTracker;
    private string _layerSourceMode = LayerSourceCoordinator.ModeAuto;

    // Press-highlight pipeline: per-layer (mod-set + keycode) → KeyViewModel
    // lookup, held-modifier set, modifier-grace deferral, per-key pulse.
    // Built lazily once the Keys collection is populated.
    private KeyHighlightTracker? _highlightTracker;

    // --- UI-bindable state ---
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusMessageFull))]
    private string _statusMessage = "";

    /// <summary>Tooltip text for the (often-truncated) status bar — full status + keyboard hint joined.</summary>
    public string StatusMessageFull => $"{StatusMessage}  ·  {KeyboardStatusHint}";

    /// <summary>
    /// Suffix appended to the keyboard status hint describing the active layer
    /// source ("via Raw HID (Go60 Left)" / "via signal macros"). Empty until
    /// the coordinator has resolved a source.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyboardStatusHint))]
    [NotifyPropertyChangedFor(nameof(StatusMessageFull))]
    private string _layerSourceHint = "";

    /// <summary>True while the HID source is the active layer source. Used by
    /// the renderer to hide pink "untrackable" overlays since every layer
    /// switch is reported by HID.</summary>
    [ObservableProperty] private bool _isHidSourceActive;
    [ObservableProperty] private bool _isAlwaysOnTop;
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
            if (TryParseRgb(PressHighlightColor, out var r, out var g, out var b))
                return $"#{(int)(r * 0.55):X2}{(int)(g * 0.55):X2}{(int)(b * 0.55):X2}";
            return AppTheme.PressHighlightStrokeFallbackHex;
        }
    }

    private static bool TryParseRgb(string hex, out int r, out int g, out int b)
    {
        r = g = b = 0;
        if (string.IsNullOrEmpty(hex)) return false;
        var s = hex.StartsWith('#') ? hex[1..] : hex;
        if (s.Length < 6) return false;
        return int.TryParse(s.AsSpan(0, 2), System.Globalization.NumberStyles.HexNumber, null, out r)
            && int.TryParse(s.AsSpan(2, 2), System.Globalization.NumberStyles.HexNumber, null, out g)
            && int.TryParse(s.AsSpan(4, 2), System.Globalization.NumberStyles.HexNumber, null, out b);
    }

    partial void OnPressHighlightColorChanged(string value) =>
        PersistSetting(s => s with { PressHighlightColor = value });

    /// <summary>
    /// Global show/hide hotkey keycode (e.g. "F12"). Modifier handling lives
    /// in <see cref="UserSettings.HotkeyModifiers"/> and isn't user-editable
    /// today. Changing this raises <see cref="HotkeyKeyChanged"/> so the live
    /// <c>GlobalHotkeyService</c> rewires without restart, and bumps the
    /// signal-picker rebuild so the new hotkey is excluded from candidates.
    /// </summary>
    [ObservableProperty]
    private string _hotkeyKey = "F12";

    partial void OnHotkeyKeyChanged(string value)
    {
        PersistSetting(s => s with { HotkeyKey = value });
        HotkeyKeyChanged?.Invoke(value);
        // Same channel SettingsViewModel listens to for layer-signal picker rebuilds.
        ManualLayerSignalsChanged?.Invoke();
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
        // Re-resolve every key's fill — &lt / &mo / signal-macro keys reference
        // arbitrary layer colors, so changing layer 2's tint repaints layer 0's view too.
        if (_config is not null)
            ApplyActiveLayer(ActiveLayerIndex);
        OnPropertyChanged(nameof(ActiveLayerTintColor));
    }

    /// <inheritdoc cref="MergedSignalTableManager.GetAutoSignalKeycodeForLayer"/>
    public string? GetAutoSignalKeycodeForLayer(int layerIndex) =>
        _signalManager.GetAutoSignalKeycodeForLayer(layerIndex);

    /// <inheritdoc cref="MergedSignalTableManager.GetManualSignalKeycodeForLayer"/>
    public string? GetManualSignalKeycodeForLayer(int layerIndex) =>
        _signalManager.GetManualSignalKeycodeForLayer(layerIndex);

    /// <summary>
    /// All signal keycodes the live tracker currently considers — auto plus
    /// active manual mappings. Diagnostic surface for the Settings list to
    /// compute "which F-keys are still free".
    /// </summary>
    public IReadOnlyDictionary<string, SignalKeyMapping> EffectiveSignalMappings =>
        _signalManager.MergedTable.Mappings;

    /// <inheritdoc cref="MergedSignalTableManager.SetManualLayerSignal"/>
    public void SetManualLayerSignal(int layerIndex, string? keycode) =>
        _signalManager.SetManualLayerSignal(layerIndex, keycode);

    /// <summary>
    /// Raised after the merged signal table is rebuilt. SettingsViewModel
    /// listens to refresh the per-layer picker rows when (a) a layout loads,
    /// (b) the keyboard profile changes, or (c) the user toggles a manual
    /// binding (which can free or claim an F-key for other layers).
    /// </summary>
    public event Action? ManualLayerSignalsChanged;

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
    [NotifyPropertyChangedFor(nameof(StatusMessageFull))]
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
    public Action? ShowAccessibilityPromptRequested { get; set; }
    public Action? OpenSettingsRequested { get; set; }
    public Action? OpenGenerateSignalsRequested { get; set; }

    /// <summary>Path of the layout JSON the user currently has loaded, or null if none.</summary>
    public string? LoadedLayoutPath => _loadedLayoutPath;

    private bool _accessibilityDialogShown;

    // --- Commands ---
    public IRelayCommand QuitCommand { get; }
    public IRelayCommand ShowCommand { get; }
    public IRelayCommand LoadLayoutCommand { get; }
    public IRelayCommand RefreshCommand { get; }
    public IRelayCommand TogglePinCommand { get; }
    public IRelayCommand ToggleLiveHighlightingCommand { get; }
    public IRelayCommand ToggleAutoLayerSwitchCommand { get; }
    public IRelayCommand OpenLogFolderCommand { get; }
    public IRelayCommand CopyDiagnosticsCommand { get; }
    public IRelayCommand<IKeyboardProfile> SelectKeyboardCommand { get; }
    public IRelayCommand DismissToastCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand OpenGenerateSignalsCommand { get; }

    private readonly SharpHookProvider? _hookProvider;
    private readonly IActiveWindowMonitor? _activeWindowMonitor;

    /// <summary>
    /// All auto-app→layer state and behaviour. Owns the session record, the
    /// exit-tap detector, the active-window monitor subscription, the rule
    /// list, and the matched-rule cache. This VM exposes facade properties
    /// over the engine so existing XAML bindings and
    /// <see cref="SettingsViewModel"/> calls keep working unchanged.
    /// </summary>
    private readonly AutoSwitchEngine _autoSwitch;

    /// <inheritdoc cref="AutoSwitchEngine.ExitTapKey"/>
    public int? ExitTapKey => _autoSwitch.ExitTapKey;

    /// <inheritdoc cref="AutoSwitchEngine.SetExitTapKey"/>
    public void SetExitTapKey(int? index) => _autoSwitch.SetExitTapKey(index);

    /// <inheritdoc cref="AutoSwitchEngine.ActiveWindow"/>
    public ActiveWindowInfo? ActiveWindow => _autoSwitch.ActiveWindow;

    /// <inheritdoc cref="AutoSwitchEngine.AppLayerRules"/>
    public ObservableCollection<AppLayerRule> AppLayerRules => _autoSwitch.AppLayerRules;

    /// <inheritdoc cref="AutoSwitchEngine.IsEnabled"/>
    public bool IsAutoSwitchKeyboardLayerEnabled
    {
        get => _autoSwitch.IsEnabled;
        set => _autoSwitch.IsEnabled = value;
    }

    /// <inheritdoc cref="AutoSwitchEngine.FallbackMode"/>
    public AutoSwitchFallbackMode AutoSwitchFallbackMode
    {
        get => _autoSwitch.FallbackMode;
        set => _autoSwitch.FallbackMode = value;
    }

    /// <inheritdoc cref="AutoSwitchEngine.MatchedAppLayerRule"/>
    public AppLayerRule? MatchedAppLayerRule => _autoSwitch.MatchedAppLayerRule;

    /// <inheritdoc cref="AutoSwitchEngine.ApplyAppLayerRules"/>
    public void ApplyAppLayerRules(IReadOnlyList<AppLayerRule> rules) =>
        _autoSwitch.ApplyAppLayerRules(rules);

    public MainWindowViewModel(
        ISettingsService settingsService,
        SharpHookProvider? hookProvider = null,
        IActiveWindowMonitor? activeWindowMonitor = null)
    {
        _settingsService = settingsService;
        _hookProvider = hookProvider;
        _activeWindowMonitor = activeWindowMonitor;
        var s = settingsService.Load();
        _profile = KeyboardProfileRegistry.TryResolve(s.Keyboard, out var p) ? p : new Go60Profile();
        _selectedKeyboard = _profile;
        _isAlwaysOnTop = s.AlwaysOnTop;
        _isLiveHighlightingEnabled = s.LiveKeyHighlighting;
        _isAutoLayerSwitchEnabled = s.AutoLayerSwitch;
        _backgroundOpacity = Math.Clamp(s.BackgroundOpacity, 0.0, 1.0);
        if (!string.IsNullOrWhiteSpace(s.PressHighlightColor))
            _pressHighlightColor = s.PressHighlightColor;
        if (!string.IsNullOrWhiteSpace(s.HotkeyKey))
            _hotkeyKey = s.HotkeyKey;
        _isStackedLayout = s.StackedLayout;
        _stackedTopHand = string.IsNullOrWhiteSpace(s.StackedTopHand) ? "Left" : s.StackedTopHand;
        _layerSourceMode = string.IsNullOrWhiteSpace(s.LayerSource) ? LayerSourceCoordinator.ModeAuto : s.LayerSource;
        // Seed the static palette with persisted per-keyboard, per-layer overrides
        // so the very first paint already reflects the user's customization.
        LayerColorPalette.SetOverrides(s.LayerColors);

        // Merged signal-table manager. Owns the auto + manual layer-signal
        // mappings; we push the merged table into the live tracker (if one
        // exists) and bump the settings picker on every rebuild.
        _signalManager = new MergedSignalTableManager(settingsService, _profile.Id);
        _signalManager.MergedTableChanged += merged =>
        {
            _tracker?.UpdateTable(merged);
            ManualLayerSignalsChanged?.Invoke();
        };

        // Auto-app→layer engine. Owns rule list, session state, exit-tap
        // detector, and the active-window monitor subscription. Engine fires
        // PushLayerRequested when it wants the host to change layers; we
        // relay its PropertyChanged events under this VM's property names
        // so existing XAML bindings keep working unchanged.
        _autoSwitch = new AutoSwitchEngine(
            settingsService,
            activeWindowMonitor,
            () => ActiveLayerIndex,
            _profile);
        _autoSwitch.PushLayerRequested += PushLayerToKeyboard;
        _autoSwitch.PropertyChanged += (_, e) =>
        {
            var relay = e.PropertyName switch
            {
                nameof(AutoSwitchEngine.IsEnabled) => nameof(IsAutoSwitchKeyboardLayerEnabled),
                nameof(AutoSwitchEngine.FallbackMode) => nameof(AutoSwitchFallbackMode),
                nameof(AutoSwitchEngine.ActiveWindow) => nameof(ActiveWindow),
                nameof(AutoSwitchEngine.MatchedAppLayerRule) => nameof(MatchedAppLayerRule),
                nameof(AutoSwitchEngine.ExitTapKey) => nameof(ExitTapKey),
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
        ToggleAutoLayerSwitchCommand = new RelayCommand(() =>
        {
            IsAutoLayerSwitchEnabled = !IsAutoLayerSwitchEnabled;
            PersistSetting(s2 => s2 with { AutoLayerSwitch = IsAutoLayerSwitchEnabled });
            if (!IsAutoLayerSwitchEnabled)
                ResetLayerState();
        });
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
                _loadStatusBase = null;
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
        OpenGenerateSignalsCommand = new RelayCommand(() => OpenGenerateSignalsRequested?.Invoke());

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
            _loadStatusBase = null;
            StatusMessage = Loc.Instance["Status_NoLayoutLoaded"];
        }

        // Live tracking is no longer Linux-blocked: the HID source works
        // without any global hook, and StartKeyEventTracking() internally
        // skips SharpHook when _hookProvider is null (which it is on Linux).
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
                    _layerCoordinator?.SetActiveProfile(matching);
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
            _signalMacros = SignalMacroScanner.DetectSignalMacros(config);
            _untrackable = SignalMacroScanner.FindUntrackableLayerSwitches(config, _signalMacros);
            _bindingResolver = new LayerBindingResolver(config, _signalMacros);
            _signalManager.SetAutoTable(LayerSignalTable.Build(config, _signalMacros));

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
            _loadStatusBase = baseMsg;
            StatusMessage = ComposeLoadStatus();
            DiagnosticLog.Info("MainVM",
                $"Loaded '{path}' signalMacros={_signalMacros.Count} untrackable={_untrackable.Count}");
        }
        catch (Exception ex)
        {
            _loadStatusBase = null;
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
    /// signal-macro count, untrackable layer-switch list, last load error)
    /// for inclusion in <see cref="DiagnosticLog.CollectDiagnosticReport"/>.
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
        sb.AppendLine($"Signal macros detected: {_signalMacros.Count}");
        sb.AppendLine($"Untrackable layer switches: {_untrackable.Count}");
        if (_untrackable.Count > 0)
        {
            foreach (var u in _untrackable)
            {
                var target = u.TargetLayer is int t ? t.ToString() : "?";
                sb.AppendLine($"  (layer {u.LayerIndex}, key index {u.KeyIndex}) {u.Behavior} {target}");
            }
        }

        return sb.ToString();
    }

    /// <summary>Gracefully stops live tracking; idempotent. Called on window close / quit.</summary>
    public void Shutdown()
    {
        StopKeyEventTracking();
        _autoSwitch.Shutdown();
    }

    // --- Internals ---

    private void BuildKeysFromProfile()
    {
        Keys.Clear();
        LeftKeys.Clear();
        RightKeys.Clear();

        double lMinX = double.PositiveInfinity, lMinY = double.PositiveInfinity;
        double lMaxX = double.NegativeInfinity, lMaxY = double.NegativeInfinity;
        double rMinX = double.PositiveInfinity, rMinY = double.PositiveInfinity;
        double rMaxX = double.NegativeInfinity, rMaxY = double.NegativeInfinity;

        foreach (var pos in _profile.Keys)
        {
            var vm = new KeyViewModel(pos);
            Keys.Add(vm);
            if (pos.Hand == Hand.Left)
            {
                LeftKeys.Add(vm);
                if (pos.X < lMinX) lMinX = pos.X;
                if (pos.Y < lMinY) lMinY = pos.Y;
                if (pos.X + pos.Width > lMaxX) lMaxX = pos.X + pos.Width;
                if (pos.Y + pos.Height > lMaxY) lMaxY = pos.Y + pos.Height;
            }
            else
            {
                RightKeys.Add(vm);
                if (pos.X < rMinX) rMinX = pos.X;
                if (pos.Y < rMinY) rMinY = pos.Y;
                if (pos.X + pos.Width > rMaxX) rMaxX = pos.X + pos.Width;
                if (pos.Y + pos.Height > rMaxY) rMaxY = pos.Y + pos.Height;
            }
        }

        _leftBounds = LeftKeys.Count > 0 ? (lMinX, lMinY, lMaxX, lMaxY) : (0, 0, 0, 0);
        _rightBounds = RightKeys.Count > 0 ? (rMinX, rMinY, rMaxX, rMaxY) : (0, 0, 0, 0);

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
        _autoSwitch.SetActiveProfile(profile);
        _signalManager.SetActiveProfile(profile.Id);

        var layoutFits = _config is not null
            && _config.LayerCount > 0
            && _config.Layers[0].Bindings.Count == profile.KeyCount;

        if (_config is not null && !layoutFits)
        {
            _config = null;
            _signalMacros = Array.Empty<SignalMacro>();
            _untrackable = Array.Empty<UntrackableLayerSwitch>();
            _bindingResolver = null;
            _signalManager.SetAutoTable(new LayerSignalTable(new Dictionary<string, SignalKeyMapping>()));
            Layers.Clear();
            ActiveLayerIndex = 0;
            HasLayoutLoaded = false;
            _loadStatusBase = null;
            StatusMessage = Loc.Instance.Format("Status_KeyboardSwitchedUnloaded", profile.DisplayName);
        }
        else if (_config is not null)
        {
            ApplyActiveLayer(ActiveLayerIndex);
            _loadStatusBase = null;
            StatusMessage = Loc.Instance.Format("Status_KeyboardSwitched", profile.DisplayName);
        }
        else
        {
            _loadStatusBase = null;
            StatusMessage = Loc.Instance.Format("Status_KeyboardSwitched", profile.DisplayName);
        }

        PersistSetting(s => s with { Keyboard = profile.Id });
        DiagnosticLog.Info("MainVM", $"Keyboard profile switched to {profile.Id}");

        // Re-scope HID discovery to the new profile so a Go60 stops feeding
        // reports into a Glove80 layout (or vice versa). No-op when HID is
        // disabled or the source isn't running.
        _layerCoordinator?.SetActiveProfile(profile);

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

    public void PushLayerToKeyboard(int index)
    {
        var sender = _commandSender;
        if (sender is null) return;

        // Firmware keeps layer 0 active regardless; bitmask just names the target.
        uint bitmask = 1u << index;
        // Arm before sending so the inbound 0xFF reply can be matched as ours.
        // LayerStateTracker OR's bit 0 in for the firmware's always-on layer 0.
        _layerStateTracker?.ExpectAppState(bitmask);
        _ = Task.Run(async () =>
        {
            try
            {
                await sender.SetLayerStateAsync(bitmask, CancellationToken.None);
                DiagnosticLog.Info("LayerPush", $"sent layer {index} (mask 0x{bitmask:X})");
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("LayerPush", $"layer {index} failed: {ex.Message}");
            }
        });
    }

    private void OnLayerStateConfirmed(uint bitmask)
    {
        var tracker = _layerStateTracker;
        if (tracker is null) return;
        if (tracker.IsAppControlled)
            DiagnosticLog.Info("LayerState", $"app push acknowledged: layer {tracker.HighestActiveLayer} (mask 0x{bitmask:X})");
        else
            DiagnosticLog.Info("LayerState", $"external change: layer {tracker.HighestActiveLayer} (mask 0x{bitmask:X})");
    }

    private void ApplyActiveLayer(int index)
    {
        if (_config is null) return;
        if (index < 0 || index >= _config.Layers.Count) return;

        ActiveLayerIndex = index;
        var layer = _config.Layers[index];

        // Includes hold-tap names whose hold side is a signal macro, so an
        // &ht_* layer binding resolves to its underlying SignalMacro.
        var signalByName = LayerSignalTable.BuildSignalLookup(_config, _signalMacros);
        var holdTapByName = _config.HoldTaps.ToDictionary(h => h.Name, StringComparer.Ordinal);
        var untrackableSet = new HashSet<(int layer, int key)>(_untrackable.Select(u => (u.LayerIndex, u.KeyIndex)));

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

            var isSignal = signalByName.TryGetValue(binding.Behavior, out var signalMacro);
            holdTapByName.TryGetValue(binding.Behavior, out var holdTap);
            var targetLayer = LayerBindingResolver.ResolveTargetLayer(binding, isSignal ? signalMacro : null, holdTap);
            var targetLayerName = targetLayer is int tl && tl >= 0 && tl < _config.Layers.Count
                ? _config.Layers[tl].Name
                : null;
            Keys[i].ApplyBinding(
                binding,
                isSignalMacro: isSignal,
                // HID source reports every layer change directly, so the
                // pink "untrackable" warning is meaningless when it's active.
                isUntrackable: !IsHidSourceActive && untrackableSet.Contains((layer.Index, i)),
                targetLayer: targetLayer,
                targetLayerName: targetLayerName,
                profileId: _profile.Id,
                holdTap: holdTap,
                signal: isSignal ? signalMacro : null);
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

        HighlightTracker.Rebuild(_config, _bindingResolver, signalByName);
    }

    /// <summary>
    /// Lazily-instantiated press-highlight tracker. Constructed on first use
    /// so it captures the <see cref="Keys"/> collection after
    /// <see cref="BuildKeysFromProfile"/> has populated it.
    /// </summary>
    private KeyHighlightTracker HighlightTracker =>
        _highlightTracker ??= new KeyHighlightTracker(Keys, () => ActiveLayerIndex, () => IsHidSourceActive);


    private void ToggleLiveHighlighting()
    {
        IsLiveHighlightingEnabled = !IsLiveHighlightingEnabled;
        PersistSetting(s2 => s2 with { LiveKeyHighlighting = IsLiveHighlightingEnabled });

        if (IsLiveHighlightingEnabled)
            StartKeyEventTracking();
        else
            StopKeyEventTracking();
    }

    private void StartKeyEventTracking()
    {
        if (_layerCoordinator is not null) return;
        // Re-arm the accessibility-prompt latch on every start so a later
        // failure (perms revoked at runtime, hook restart) can prompt again.
        _accessibilityDialogShown = false;

        HotkeyLayerTrackerLayerSource? hotkeyWrapper = null;
        if (_hookProvider is not null)
        {
            try
            {
                var source = new SharpHookKeyEventSource(_hookProvider);
                source.HookFailed += OnHookFailed;
                _keyEventSource = source;
                _tracker = new HotkeyLayerTracker(_keyEventSource, _signalManager.MergedTable);
                _tracker.KeyObserved += HighlightTracker.OnHookEvent;
                _keyEventSource.Start();
                hotkeyWrapper = new HotkeyLayerTrackerLayerSource(_tracker);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Error("MainVM", $"SharpHook init failed: {ex.Message}");
                _keyEventSource?.Dispose();
                _keyEventSource = null;
                _tracker = null;
                hotkeyWrapper = null;
            }
        }

        // Raw HID is platform-agnostic and doesn't need accessibility perms,
        // so it spins up regardless of the SharpHook outcome above. The
        // matcher scopes discovery to the user's selected keyboard (both
        // Moergo boards share VID:PID, so we'd otherwise latch onto whichever
        // is enumerated first). Per-OS transport selection (IOKit / hidraw /
        // HidSharp+WinRT GATT) lives inside ZmkHidProtocol's LayerSourceFactory.
        var (hidSource, hidSink) = LayerSourceFactory.Create(new KeyboardProfileMatcher(_profile));
        _commandSender = new CommandSender(hidSource, hidSink);
        _layerStateTracker = new LayerStateTracker();
        hidSource.ReportReceived += _layerStateTracker.OnReport;
        _layerStateTracker.StateChanged += OnLayerStateConfirmed;

        _layerCoordinator = new LayerSourceCoordinator(hidSource, hotkeyWrapper, _layerSourceMode);
        _layerCoordinator.ActiveLayerChanged += OnActiveLayerChanged;
        _layerCoordinator.ActiveKeyPositionEvent += OnKeyPositionFromHid;
        _layerCoordinator.ActiveSourceChanged += OnActiveSourceChanged;
        _layerCoordinator.Start();
        // Initial label sync — the coordinator may already have settled the
        // active source before our subscription was attached above.
        OnActiveSourceChanged();
    }

    private void OnHookFailed(Exception ex)
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_accessibilityDialogShown) return;
        _accessibilityDialogShown = true;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowAccessibilityPromptRequested?.Invoke());
    }

    private void StopKeyEventTracking()
    {
        _commandSender?.Dispose();
        _commandSender = null;
        if (_layerStateTracker is not null)
        {
            _layerStateTracker.StateChanged -= OnLayerStateConfirmed;
            _layerStateTracker = null;
        }
        if (_layerCoordinator is not null)
        {
            _layerCoordinator.ActiveLayerChanged -= OnActiveLayerChanged;
            _layerCoordinator.ActiveKeyPositionEvent -= OnKeyPositionFromHid;
            _layerCoordinator.ActiveSourceChanged -= OnActiveSourceChanged;
            _layerCoordinator.Dispose();
            _layerCoordinator = null;
        }
        if (_tracker is not null)
        {
            _tracker.KeyObserved -= HighlightTracker.OnHookEvent;
            _tracker.Dispose();
            _tracker = null;
        }
        if (_keyEventSource is SharpHookKeyEventSource sh)
            sh.HookFailed -= OnHookFailed;
        _keyEventSource?.Dispose();
        _keyEventSource = null;
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
        if (_layerCoordinator is null) return;
        var hidActive = _layerCoordinator.IsHidActive;
        var label = _layerCoordinator.ActiveSourceLabel;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var flipped = IsHidSourceActive != hidActive;
            IsHidSourceActive = hidActive;
            LayerSourceHint = string.IsNullOrEmpty(label)
                ? ""
                : Loc.Instance.Format("Status_LayerSourceHintFormat", label);
            // Pink "untrackable" overlays are gated on !IsHidSourceActive; rebuild
            // the per-key state so the change takes effect immediately.
            if (flipped) ApplyActiveLayer(ActiveLayerIndex);
            // The "N layer switches not tracked" suffix only applies when
            // SharpHook is the source — HID reports every layer change, so
            // recompose to drop/restore that suffix on flips.
            if (flipped && _loadStatusBase is not null)
                StatusMessage = ComposeLoadStatus();
        });
    }

    private string ComposeLoadStatus()
    {
        var s = _loadStatusBase ?? "";
        if (_untrackable.Count > 0 && !IsHidSourceActive)
            s += " — " + Loc.Instance.Format("Status_UntrackableLayersFormat", _untrackable.Count);
        return s;
    }

    /// <summary>
    /// Press-highlight path for the HID source. Bypasses _zmkLookup entirely
    /// — the firmware reports the physical matrix position so we go straight
    /// to <see cref="Keys"/>[position]. No modifier-grace logic needed
    /// (HID never reports synthesized modifiers as separate events).
    /// </summary>
    private void OnKeyPositionFromHid(int position, bool pressed)
    {
        // Feed the auto-switch engine's exit-tap detector regardless of
        // pressed/released — it needs releases to re-arm. Null position
        // short-circuits inside the detector, so this is free when no
        // exit key is configured.
        _autoSwitch.OnKeyPositionEvent(position, pressed);

        if (!pressed) return;
        HighlightTracker.PulseAt(position);
    }

    /// <summary>Called by SettingsViewModel when the user picks a different layer source mode.</summary>
    public void SetLayerSourceMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        if (mode == _layerSourceMode) return;
        _layerSourceMode = mode;
        PersistSetting(s => s with { LayerSource = mode });
        _layerCoordinator?.SetMode(mode);
    }

    public string LayerSourceMode => _layerSourceMode;

    private void ResetLayerState()
    {
        _tracker?.Reset();
        _highlightTracker?.Reset();
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
