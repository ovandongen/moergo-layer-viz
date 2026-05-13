using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.Core.Settings;

namespace MoergoLayerViz.App.ViewModels;

/// <summary>
/// View model for the Settings window. Thin shell that forwards property
/// changes to <see cref="MainWindowViewModel"/> — the main VM owns the
/// state and persistence. Bindings here are TwoWay so live tweaks update
/// the board immediately.
/// </summary>
public partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly MainWindowViewModel _mainViewModel;
    private bool _disposed;

    public SettingsViewModel(ISettingsService settingsService, MainWindowViewModel mainViewModel)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _mainViewModel.PropertyChanged += OnMainPropertyChanged;
        _mainViewModel.Layers.CollectionChanged += OnLayersCollectionChanged;
        _mainViewModel.AppLayerRules.CollectionChanged += OnAppLayerRulesCollectionChanged;
        _mainViewModel.ManualLayerSignalsChanged += RebuildLayerEntries;
        LayerSourceChoices = BuildLayerSourceChoices();
        RebuildLayerEntries();
        RebuildAppLayerRuleRows();
        RefreshRunningProcesses();
    }

    /// <summary>(label, value) pair for the layer-source dropdown. Label is pre-localized; value is the canonical mode string persisted in settings.</summary>
    public sealed record LayerSourceChoice(string Label, string Value);

    /// <summary>Choices for the layer-source dropdown. Built once at construction; reopen Settings after a culture change to pick up new labels.</summary>
    public IReadOnlyList<LayerSourceChoice> LayerSourceChoices { get; }

    private static IReadOnlyList<LayerSourceChoice> BuildLayerSourceChoices() => new[]
    {
        new LayerSourceChoice(Loc.Instance["Settings_LayerSource_Auto"], LayerSourceCoordinator.ModeAuto),
        new LayerSourceChoice(Loc.Instance["Settings_LayerSource_RawHid"], LayerSourceCoordinator.ModeRawHid),
        new LayerSourceChoice(Loc.Instance["Settings_LayerSource_SharpHook"], LayerSourceCoordinator.ModeSharpHook),
    };

    /// <summary>Currently selected layer-source mode. Two-way bound; setter persists + reconfigures the coordinator via the main VM.</summary>
    public LayerSourceChoice? SelectedLayerSourceChoice
    {
        get => LayerSourceChoices.FirstOrDefault(c => c.Value == _mainViewModel.LayerSourceMode)
               ?? LayerSourceChoices[0];
        set
        {
            if (value is null) return;
            _mainViewModel.SetLayerSourceMode(value.Value);
            OnPropertyChanged();
        }
    }

    /// <summary>"via Raw HID (Go60 Left)" / "via signal macros" — currently active source. Updated live as the coordinator hot-swaps.</summary>
    public string LayerSourceStatus => _mainViewModel.LayerSourceHint;

    // Testing tab — temporary scratch area for poking at HID push without polluting the main page.
    public ObservableCollection<LayerViewModel> TestLayers => _mainViewModel.Layers;

    private LayerViewModel? _selectedTestLayer;
    public LayerViewModel? SelectedTestLayer
    {
        get => _selectedTestLayer;
        set
        {
            if (SetProperty(ref _selectedTestLayer, value) && value is not null)
                _mainViewModel.PushLayerToKeyboard(value.Index);
        }
    }

    // Active-window passthroughs (Phase 1 diagnostic surface on the Testing
    // tab). Re-raised inside OnMainPropertyChanged when ActiveWindow changes
    // on the main VM.
    public string ActiveProcessName => _mainViewModel.ActiveWindow?.ProcessName ?? "(none)";
    public string ActiveBundleId => _mainViewModel.ActiveWindow?.BundleId ?? "(none)";
    public string ActiveWindowTitle => _mainViewModel.ActiveWindow?.WindowTitle ?? "(none)";

    // --- Auto-switch (Phase 2: author + persist rules, preview match;
    //                   Phase 3 Slice A: master toggle drives firing + monitor gating) ---

    /// <summary>Two-way pass-through to <see cref="MainWindowViewModel.IsAutoSwitchKeyboardLayerEnabled"/>.
    /// Master on/off for the auto-switch engine.</summary>
    public bool IsAutoSwitchKeyboardLayerEnabled
    {
        get => _mainViewModel.IsAutoSwitchKeyboardLayerEnabled;
        set
        {
            if (_mainViewModel.IsAutoSwitchKeyboardLayerEnabled == value) return;
            _mainViewModel.IsAutoSwitchKeyboardLayerEnabled = value;
            OnPropertyChanged();
        }
    }

    // Fallback mode — bound to two radio buttons. Avalonia ToggleButton's
    // IsChecked binds bidirectionally to a bool per radio; both radios share
    // the GroupName so only one is ever true. Each property routes
    // through MainWindowViewModel.AutoSwitchFallbackMode (persisted per-keyboard).

    public bool IsFallbackPrevious
    {
        get => string.Equals(_mainViewModel.AutoSwitchFallbackMode,
            MainWindowViewModel.AutoSwitchFallbackPrevious, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!value) return;
            if (IsFallbackPrevious) return;
            _mainViewModel.AutoSwitchFallbackMode = MainWindowViewModel.AutoSwitchFallbackPrevious;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFallbackBase));
        }
    }

    public bool IsFallbackBase
    {
        get => string.Equals(_mainViewModel.AutoSwitchFallbackMode,
            MainWindowViewModel.AutoSwitchFallbackBase, StringComparison.OrdinalIgnoreCase);
        set
        {
            if (!value) return;
            if (IsFallbackBase) return;
            _mainViewModel.AutoSwitchFallbackMode = MainWindowViewModel.AutoSwitchFallbackBase;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFallbackPrevious));
        }
    }

    /// <summary>Human-readable summary of the active keyboard's exit-tap
    /// key (e.g. "#42") or a localized "(none)" when unset. Drives the
    /// label next to the "Pick…" button.</summary>
    public string ExitTapSummary
    {
        get
        {
            var key = _mainViewModel.ExitTapKey;
            if (key is null) return Loc.Instance["Settings_AutoSwitch_ExitKeysNone"];
            return $"#{key.Value}";
        }
    }

    /// <summary>True when an exit-tap key is configured for the active
    /// keyboard. Drives the IsEnabled state of the Clear button.</summary>
    public bool HasExitTap => _mainViewModel.ExitTapKey is not null;

    /// <summary>Snapshot of the currently-configured exit-tap key, used by
    /// the SettingsWindow code-behind to seed the picker dialog.</summary>
    public int? ExitTapKey => _mainViewModel.ExitTapKey;

    /// <summary>Applies a freshly-picked exit-tap key. Called by the
    /// SettingsWindow code-behind after the picker dialog closes with an
    /// OK result.</summary>
    public void ApplyExitTapKey(int? index)
    {
        _mainViewModel.SetExitTapKey(index);
    }

    [RelayCommand]
    private void ClearExitTap() => _mainViewModel.SetExitTapKey(null);

    /// <summary>Active keyboard profile. The SettingsWindow code-behind reads
    /// this when building an <see cref="ExitKeyPickerViewModel"/> so the
    /// picker renders the keyboard the user is configuring.</summary>
    public Core.Layout.IKeyboardProfile ActiveKeyboardProfile => _mainViewModel.SelectedKeyboard;


    /// <summary>UI-side rule list. Wraps each <see cref="AppLayerRule"/> with a
    /// pre-formatted <c>LayerLabel</c>. Rebuilt when the underlying rule list
    /// changes (add/remove) or when <c>Layers</c> changes (keymap reload).</summary>
    public ObservableCollection<AppLayerRuleRow> AppLayerRuleRows { get; } = new();

    /// <summary>Layers available as rule targets — same source as the Testing tab combo.</summary>
    public ObservableCollection<LayerViewModel> AutoSwitchLayers => _mainViewModel.Layers;

    /// <summary>Text typed into the "new rule" process-name field. Capped at
    /// 200 chars in the XAML; keeps the settings file from ballooning on
    /// malformed input.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAppLayerRuleCommand))]
    private string _newRuleProcessMatch = "";

    /// <summary>Layer selected in the "new rule" dropdown.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddAppLayerRuleCommand))]
    private LayerViewModel? _newRuleLayer;

    private bool CanAddAppLayerRule() =>
        !string.IsNullOrWhiteSpace(NewRuleProcessMatch) && NewRuleLayer is not null;

    [RelayCommand(CanExecute = nameof(CanAddAppLayerRule))]
    private void AddAppLayerRule()
    {
        if (NewRuleLayer is null) return;
        _mainViewModel.AddAppLayerRule(NewRuleProcessMatch, NewRuleLayer.Index);
        NewRuleProcessMatch = "";
    }

    [RelayCommand]
    private void RemoveAppLayerRule(AppLayerRuleRow? row)
    {
        if (row is null) return;
        _mainViewModel.RemoveAppLayerRule(row.Rule);
    }

    [RelayCommand]
    private void MoveAppLayerRuleUp(AppLayerRuleRow? row)
    {
        if (row is null) return;
        _mainViewModel.MoveAppLayerRule(row.Rule, -1);
    }

    [RelayCommand]
    private void MoveAppLayerRuleDown(AppLayerRuleRow? row)
    {
        if (row is null) return;
        _mainViewModel.MoveAppLayerRule(row.Rule, +1);
    }

    /// <summary>
    /// Localized "Would fire: layer N" readout for the currently focused app,
    /// or a "no match" hint. Phase 3 will turn this into the actual push.
    /// </summary>
    public string WouldFireText
    {
        get
        {
            var match = _mainViewModel.MatchedAppLayerRule;
            if (match is null) return Loc.Instance["Settings_AutoSwitch_WouldFireNone"];
            return Loc.Instance.Format("Settings_AutoSwitch_WouldFireFormat", FormatLayerLabel(match.LayerIndex));
        }
    }

    /// <summary>"Symbol (L:1)" for a known layer, or "(invalid layer)" if the
    /// rule points at an index no longer present in the active keymap. Shared
    /// by the rules-list rows and the WouldFire readout.</summary>
    private string FormatLayerLabel(int layerIndex)
    {
        var layer = _mainViewModel.Layers.FirstOrDefault(l => l.Index == layerIndex);
        if (layer is null) return Loc.Instance["Settings_AutoSwitch_LayerInvalid"];
        return Loc.Instance.Format("Settings_AutoSwitch_LayerFormat", layer.DisplayName, layer.Index);
    }

    private void RebuildAppLayerRuleRows()
    {
        AppLayerRuleRows.Clear();
        foreach (var r in _mainViewModel.AppLayerRules)
            AppLayerRuleRows.Add(new AppLayerRuleRow(r, FormatLayerLabel(r.LayerIndex)));
    }

    private void OnAppLayerRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildAppLayerRuleRows();

    // --- Process picker -------------------------------------------------

    /// <summary>Full snapshot of running-process names, dedup'd + sorted. Source
    /// of truth for the visible <see cref="RunningProcessNames"/> after the
    /// filter is applied.</summary>
    private readonly List<string> _allRunningProcessNames = new();

    /// <summary>Filtered, visible picker list. Case-insensitive substring match
    /// against <see cref="ProcessFilter"/>; equals the full snapshot when the
    /// filter is empty.</summary>
    public ObservableCollection<string> RunningProcessNames { get; } = new();

    /// <summary>True when the filtered list is empty. XAML swaps the picker
    /// list for the localized empty-state hint when this is false.</summary>
    public bool HasRunningProcesses => RunningProcessNames.Count > 0;

    /// <summary>Filter text typed into the picker. Live-applied to the visible
    /// list on every keystroke.</summary>
    [ObservableProperty]
    private string _processFilter = "";

    partial void OnProcessFilterChanged(string value) => ApplyProcessFilter();

    /// <summary>Invoked by the SettingsWindow code-behind right before the
    /// picker flyout opens. Cross-OS via <see cref="System.Diagnostics.Process.GetProcesses"/>
    /// — same source as <c>IActiveWindowMonitor.ProcessName</c>, so what the
    /// user picks here matches what the monitor will report later. Clears the
    /// active filter so each open starts fresh.</summary>
    public void RefreshRunningProcesses()
    {
        var names = new List<string>();
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                var name = p.ProcessName;
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
            catch
            {
                // Access denied on protected processes is normal — skip.
            }
            finally
            {
                p.Dispose();
            }
        }
        _allRunningProcessNames.Clear();
        _allRunningProcessNames.AddRange(names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
        ProcessFilter = "";
        ApplyProcessFilter();
    }

    private void ApplyProcessFilter()
    {
        var filter = ProcessFilter?.Trim() ?? "";
        RunningProcessNames.Clear();
        foreach (var n in _allRunningProcessNames)
        {
            if (filter.Length == 0 ||
                n.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                RunningProcessNames.Add(n);
            }
        }
        OnPropertyChanged(nameof(HasRunningProcesses));
    }

    /// <summary>Picker item selected → drop the name into the textbox. The
    /// flyout closes itself on selection (Avalonia default).</summary>
    [RelayCommand]
    private void PickProcess(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        NewRuleProcessMatch = name!;
    }

    /// <summary>
    /// Detaches the long-lived <see cref="MainWindowViewModel"/> event hooks
    /// so this VM (and its window's whole DataContext graph) becomes
    /// collectible. Settings reopens build a fresh instance every time, so
    /// without this every closed-then-reopened window leaked a zombie VM
    /// still firing on every property change of the main VM.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mainViewModel.PropertyChanged -= OnMainPropertyChanged;
        _mainViewModel.Layers.CollectionChanged -= OnLayersCollectionChanged;
        _mainViewModel.AppLayerRules.CollectionChanged -= OnAppLayerRulesCollectionChanged;
        _mainViewModel.ManualLayerSignalsChanged -= RebuildLayerEntries;
    }

    public double BackgroundOpacity
    {
        get => _mainViewModel.BackgroundOpacity;
        set => _mainViewModel.BackgroundOpacity = value;
    }

    public string BackgroundOpacityPercent => $"{(int)(BackgroundOpacity * 100)}%";

    /// <summary>
    /// Press-highlight color exposed as Avalonia <see cref="Color"/> so the
    /// <c>ColorView</c> picker can bind directly. Round-trips through the
    /// main VM's hex string (single source of truth + persistence).
    /// </summary>
    public Color PressHighlightColor
    {
        get => Color.TryParse(_mainViewModel.PressHighlightColor, out var c) ? c : Colors.Yellow;
        set => _mainViewModel.PressHighlightColor = $"#{value.R:X2}{value.G:X2}{value.B:X2}";
    }

    /// <summary>Hex string for the swatch preview Border in the picker button.</summary>
    public string PressHighlightColorHex => _mainViewModel.PressHighlightColor;

    /// <summary>Currently-bound global show/hide hotkey (e.g. "F12"). Two-way bound to the main VM, which persists + rewires the live service.</summary>
    public string HotkeyKey
    {
        get => _mainViewModel.HotkeyKey;
        set => _mainViewModel.HotkeyKey = value;
    }

    /// <summary>F-key choices offered for the global hotkey. Same rationale as the layer-key list (F13–F24 first, then F1–F12).</summary>
    public static IReadOnlyList<string> HotkeyKeyChoices { get; } =
        Enumerable.Range(13, 12).Select(n => $"F{n}")
            .Concat(Enumerable.Range(1, 12).Select(n => $"F{n}"))
            .ToArray();

    /// <summary>False on Linux — Wayland blocks process-global hooks from unfocused windows, so the show/hide hotkey isn't wired and its UI is hidden.</summary>
    public static bool IsGlobalHotkeySupported { get; } = !OperatingSystem.IsLinux();

    /// <summary>Top-hand picker choices for stacked layout. Mirrors svalboard's UX.</summary>
    public IReadOnlyList<string> AvailableTopHands { get; } = new[] { "Left", "Right" };

    /// <summary>Whether the stacked layout is enabled. Drives the IsEnabled state of the top-hand picker.</summary>
    public bool IsStackedLayout => _mainViewModel.IsStackedLayout;

    /// <summary>Which half ("Left"/"Right") sits on top in stacked mode. Two-way bound to the main VM, which persists.</summary>
    public string StackedTopHand
    {
        get => _mainViewModel.StackedTopHand;
        set => _mainViewModel.StackedTopHand = value;
    }

    /// <summary>Combined per-layer rows for the Layers tab: each row has a color picker + signal-key picker.</summary>
    public ObservableCollection<LayerSettingsEntry> LayerEntries { get; } = new();

    /// <summary>"Layers — {profile name}" — section heading scoped to active keyboard.</summary>
    [ObservableProperty]
    private string _layersHeader = "";

    /// <summary>F-keys offered to the user as manual signal-keycode candidates. F13–F24 are the host-unused range and lead the list; F1–F12 follow so a user who knows what they're doing can still bind one.</summary>
    private static readonly string[] CandidateSignalKeycodes =
        Enumerable.Range(13, 12).Select(n => $"F{n}")
            .Concat(Enumerable.Range(1, 12).Select(n => $"F{n}"))
            .ToArray();

    /// <summary>True iff a layout is loaded — toggles between the entries list and the placeholder hint.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLayersHint))]
    private bool _hasLayoutForLayers;

    /// <summary>Inverse of <see cref="HasLayoutForLayers"/> for XAML visibility (Avalonia's compiled-binding `!` is finicky in non-trivial templates).</summary>
    public bool ShowsLayersHint => !HasLayoutForLayers;

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.BackgroundOpacity))
        {
            OnPropertyChanged(nameof(BackgroundOpacity));
            OnPropertyChanged(nameof(BackgroundOpacityPercent));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.PressHighlightColor))
        {
            OnPropertyChanged(nameof(PressHighlightColor));
            OnPropertyChanged(nameof(PressHighlightColorHex));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.HotkeyKey))
        {
            OnPropertyChanged(nameof(HotkeyKey));
            // Hotkey changes shrink/grow the available F-key pool for layer signals.
            RebuildLayerEntries();
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsStackedLayout))
        {
            OnPropertyChanged(nameof(IsStackedLayout));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.StackedTopHand))
        {
            OnPropertyChanged(nameof(StackedTopHand));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.SelectedKeyboard))
        {
            // Header + per-profile content (color overrides, manual signals)
            // are scoped to the active profile.
            RebuildLayerEntries();
            OnPropertyChanged(nameof(ActiveKeyboardProfile));
            // Exit-tap key is per-keyboard; the main VM reloads it during
            // SelectKeyboard, but we re-raise here in case bindings haven't
            // observed the change yet.
            OnPropertyChanged(nameof(ExitTapKey));
            OnPropertyChanged(nameof(ExitTapSummary));
            OnPropertyChanged(nameof(HasExitTap));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.LayerSourceHint))
        {
            OnPropertyChanged(nameof(LayerSourceStatus));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.ActiveWindow))
        {
            OnPropertyChanged(nameof(ActiveProcessName));
            OnPropertyChanged(nameof(ActiveBundleId));
            OnPropertyChanged(nameof(ActiveWindowTitle));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.MatchedAppLayerRule))
        {
            OnPropertyChanged(nameof(WouldFireText));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.IsAutoSwitchKeyboardLayerEnabled))
        {
            OnPropertyChanged(nameof(IsAutoSwitchKeyboardLayerEnabled));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.AutoSwitchFallbackMode))
        {
            OnPropertyChanged(nameof(IsFallbackPrevious));
            OnPropertyChanged(nameof(IsFallbackBase));
        }
        else if (e.PropertyName == nameof(MainWindowViewModel.ExitTapKey))
        {
            OnPropertyChanged(nameof(ExitTapKey));
            OnPropertyChanged(nameof(ExitTapSummary));
            OnPropertyChanged(nameof(HasExitTap));
        }
    }

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildLayerEntries();
        // A keymap reload renames or renumbers layers — any rule rows that
        // had "(invalid layer)" might resolve now (and vice versa).
        RebuildAppLayerRuleRows();
    }

    /// <summary>
    /// Rebuilds the per-layer rows for the Layers tab. Each row composes a
    /// <see cref="LayerColorEntry"/> (color picker + reset) and a
    /// <see cref="LayerSignalEntry"/> (auto-detected label or manual F-key
    /// picker). Cheap to rebuild — layer counts are small, and entries are
    /// recreated rather than reused so the swatch colors stay in sync after
    /// Reset and the signal dropdowns re-derive their "taken" set.
    /// </summary>
    private void RebuildLayerEntries()
    {
        LayerEntries.Clear();
        var profile = _mainViewModel.SelectedKeyboard;
        var profileName = profile?.DisplayName ?? "";
        LayersHeader = string.Format(Loc.Instance["Settings_LayersFormat"], profileName);
        HasLayoutForLayers = _mainViewModel.Layers.Count > 0;
        if (!HasLayoutForLayers || profile is null) return;

        var noneLabel = Loc.Instance["Settings_LayerSignalsNone"];
        var autoFormat = Loc.Instance["Settings_LayerSignalsAutoFormat"];

        // Keycodes already taken by the merged effective table — auto entries
        // plus the active manual entries. We exclude these from any picker
        // except the one that owns it (added back below). The active global
        // hotkey is also excluded so the user can't accidentally bind a layer
        // to the same key that toggles the window.
        var taken = new HashSet<string>(_mainViewModel.EffectiveSignalMappings.Keys, StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(_mainViewModel.HotkeyKey))
            taken.Add(_mainViewModel.HotkeyKey);

        foreach (var layer in _mainViewModel.Layers)
        {
            var hex = LayerColorPalette.GetColor(profile.Id, layer.Index);
            var color = Color.TryParse(hex, out var c) ? c : Colors.Gray;
            var colorEntry = new LayerColorEntry(
                layer.Index,
                layer.DisplayName,
                color,
                _mainViewModel.SetLayerColorOverride,
                LayerColorPalette.GetDefaultColor);

            var auto = _mainViewModel.GetAutoSignalKeycodeForLayer(layer.Index);
            var manual = _mainViewModel.GetManualSignalKeycodeForLayer(layer.Index);
            var autoText = auto is not null ? string.Format(autoFormat, auto) : "";

            // Build per-row dropdown: "(none)" + every candidate that's free,
            // plus this layer's own current manual selection (so it's
            // selectable and shows the active value even if it's "taken").
            var choices = new List<LayerSignalChoice> { new(noneLabel, null) };
            foreach (var kc in CandidateSignalKeycodes)
            {
                var isMine = manual is not null && string.Equals(kc, manual, StringComparison.OrdinalIgnoreCase);
                if (isMine || !taken.Contains(kc))
                    choices.Add(new LayerSignalChoice(kc, kc));
            }

            var signalEntry = new LayerSignalEntry(
                layer.Index,
                layer.DisplayName,
                auto,
                autoText,
                manual,
                choices,
                _mainViewModel.SetManualLayerSignal);

            LayerEntries.Add(new LayerSettingsEntry(colorEntry, signalEntry));
        }
    }

    // ── Version & Update Check ────────────────────────────────────────────

    private const string GitHubReleasesApi = "https://api.github.com/repos/ovandongen/moergo-layer-viz/releases/latest";
    private const string GitHubReleasesPage = "https://github.com/ovandongen/moergo-layer-viz/releases/latest";

    // Static singleton: this VM is recreated on every Settings reopen, but
    // the HTTP client should outlive the window — disposing per-window would
    // tear down the underlying socket pool.
    private static readonly HttpClient _httpClient = new();

    /// <summary>
    /// Display version pulled from <see cref="AssemblyInformationalVersionAttribute"/>.
    /// Strips the <c>+commitHash</c> suffix .NET source-link adds, so a CI build
    /// stamped <c>0.1.0+abc1234</c> renders as <c>v0.1.0</c>.
    /// </summary>
    public string AppVersion
    {
        get
        {
            var info = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (info is not null)
            {
                var plus = info.IndexOf('+');
                return "v" + (plus >= 0 ? info[..plus] : info);
            }
            return "v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?");
        }
    }

    [ObservableProperty]
    private string? _updateMessage;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    /// <summary>URL the update-link TextBlock opens. Null when no update is available.</summary>
    public string? UpdateUrl { get; private set; }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateMessage = null;
        UpdateUrl = null;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, GitHubReleasesApi);
            // GitHub's API rejects requests without a User-Agent.
            request.Headers.Add("User-Agent", "MoergoLayerViz");

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
            var tagName = doc.RootElement.GetProperty("tag_name").GetString();
            var htmlUrl = doc.RootElement.GetProperty("html_url").GetString();

            if (tagName is null)
            {
                UpdateMessage = Loc.Instance["Settings_UpdateCheckFailed"];
                return;
            }

            var latestStr = tagName.TrimStart('v');
            var currentStr = AppVersion.TrimStart('v');

            // Version.TryParse only accepts Major.Minor[.Build[.Revision]] —
            // a dev build stamped "0.0.0-dev" fails to parse, so we fall
            // through to the up-to-date branch silently. Real CI builds set
            // -p:Version=<semver> and parse cleanly.
            if (Version.TryParse(latestStr, out var latest) &&
                Version.TryParse(currentStr, out var current) &&
                latest > current)
            {
                UpdateMessage = Loc.Instance.Format("Settings_UpdateAvailable", tagName);
                UpdateUrl = htmlUrl ?? GitHubReleasesPage;
            }
            else
            {
                UpdateMessage = Loc.Instance["Settings_UpToDate"];
            }
        }
        catch
        {
            UpdateMessage = Loc.Instance["Settings_UpdateCheckFailed"];
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }
}
