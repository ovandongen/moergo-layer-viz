using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
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

    /// <summary>
    /// The owning main view model. Exposed publicly so XAML bindings for
    /// pure pass-through properties (BackgroundOpacity, HotkeyKey, etc.)
    /// route directly to it instead of through facade properties on this
    /// VM. Only transforming/derived properties (radio bool ↔ enum,
    /// localized labels, layer entries) still live here.
    /// </summary>
    public MainWindowViewModel MainViewModel => _mainViewModel;

    public SettingsViewModel(ISettingsService settingsService, MainWindowViewModel mainViewModel)
    {
        _settingsService = settingsService;
        _mainViewModel = mainViewModel;
        _mainViewModel.PropertyChanged += OnMainPropertyChanged;
        _mainViewModel.Layers.CollectionChanged += OnLayersCollectionChanged;
        UpdateChecker.PropertyChanged += (_, e) =>
        {
            var relay = e.PropertyName switch
            {
                nameof(UpdateChecker.UpdateMessage) => nameof(UpdateMessage),
                nameof(UpdateChecker.IsChecking) => nameof(IsCheckingForUpdates),
                _ => null,
            };
            if (relay is not null) OnPropertyChanged(relay);
        };
        // Seed the edit buffer from the committed list. The engine reads
        // _mainViewModel.AppLayerRules; this VM reads/writes EditingRules.
        // CommitAppLayerRules() pushes the buffer back on window close.
        foreach (var r in _mainViewModel.AppLayerRules)
            EditingRules.Add(r);
        EditingRules.CollectionChanged += OnEditingRulesChanged;

        foreach (var b in _mainViewModel.GetActiveLayerViewBindings())
            EditingLayerViewHotkeys.Add(new LayerViewHotkeyRow(b));
        EditingLayerViewHotkeys.CollectionChanged += OnLayerViewHotkeysChanged;
        SyncLayerViewHotkeyErrors();

        RebuildLayerEntries();
        RefreshRunningProcesses();
    }

    /// <summary>HID source label ("Raw HID (Go60 Left)" or "Raw HID (Go60 Left) (searching)"). Updated live as the coordinator's connection changes.</summary>
    public string LayerSourceStatus => _mainViewModel.LayerSourceHint;

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

    // --- Auto-switch (Phase 2: author + persist rules, preview match;
    //                   Phase 3 Slice A: master toggle drives firing + monitor gating) ---

    // Fallback mode — bound to two radio buttons. Avalonia ToggleButton's
    // IsChecked binds bidirectionally to a bool per radio; both radios share
    // the GroupName so only one is ever true. Each property routes
    // through MainWindowViewModel.AutoSwitchFallbackMode (persisted per-keyboard).

    public bool IsFallbackPrevious
    {
        get => _mainViewModel.AutoSwitchFallbackMode == AutoSwitchFallbackMode.Previous;
        set
        {
            if (!value || IsFallbackPrevious) return;
            _mainViewModel.AutoSwitchFallbackMode = AutoSwitchFallbackMode.Previous;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsFallbackBase));
        }
    }

    public bool IsFallbackBase
    {
        get => _mainViewModel.AutoSwitchFallbackMode == AutoSwitchFallbackMode.Base;
        set
        {
            if (!value || IsFallbackBase) return;
            _mainViewModel.AutoSwitchFallbackMode = AutoSwitchFallbackMode.Base;
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


    /// <summary>
    /// In-progress edit buffer for app→layer rules. All add/remove/move
    /// commands mutate this collection; the engine only sees the changes
    /// after <see cref="CommitAppLayerRules"/> runs on window close.
    /// </summary>
    public ObservableCollection<AppLayerRule> EditingRules { get; } = new();

    /// <summary>True when the rules list is empty. Drives the empty-state
    /// hint visibility on the Auto-switch tab.</summary>
    public bool HasNoEditingRules => EditingRules.Count == 0;

    private void OnEditingRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoEditingRules));
        OnPropertyChanged(nameof(WouldFireText));
    }

    /// <summary>
    /// Pushes the edit buffer back to <see cref="MainWindowViewModel"/> so the
    /// engine sees the new ruleset and the change is persisted. Called by
    /// <see cref="Views.SettingsWindow"/> on close; same auto-commit UX as the
    /// pre-refactor live writes, just batched.
    /// </summary>
    public void CommitAppLayerRules()
    {
        _mainViewModel.ApplyAppLayerRules(EditingRules);
    }

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
        var trimmed = NewRuleProcessMatch.Trim();
        if (string.IsNullOrWhiteSpace(trimmed)) return;
        // Upsert by process name (case-insensitive) so re-adding the same app
        // with a different layer updates in place instead of appending a
        // duplicate-shadowed entry.
        for (var i = 0; i < EditingRules.Count; i++)
        {
            if (string.Equals(EditingRules[i].ProcessMatch, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                if (EditingRules[i].LayerIndex != NewRuleLayer.Index)
                    EditingRules[i] = new AppLayerRule(EditingRules[i].ProcessMatch, NewRuleLayer.Index);
                NewRuleProcessMatch = "";
                return;
            }
        }
        EditingRules.Add(new AppLayerRule(trimmed, NewRuleLayer.Index));
        NewRuleProcessMatch = "";
    }

    [RelayCommand]
    private void RemoveAppLayerRule(AppLayerRule? rule)
    {
        if (rule is null) return;
        EditingRules.Remove(rule);
    }

    [RelayCommand]
    private void MoveAppLayerRuleUp(AppLayerRule? rule) => MoveRuleBy(rule, -1);

    [RelayCommand]
    private void MoveAppLayerRuleDown(AppLayerRule? rule) => MoveRuleBy(rule, +1);

    private void MoveRuleBy(AppLayerRule? rule, int delta)
    {
        if (rule is null) return;
        var i = EditingRules.IndexOf(rule);
        if (i < 0) return;
        var j = i + delta;
        if (j < 0 || j >= EditingRules.Count) return;
        EditingRules.Move(i, j);
    }

    /// <summary>
    /// Localized "Would fire: layer N" readout for the currently focused app.
    /// Resolved against <see cref="EditingRules"/> (not the engine's committed
    /// list) so the preview reflects in-progress edits before they're
    /// committed on close.
    /// </summary>
    public string WouldFireText
    {
        get
        {
            var match = AppLayerRuleMatcher.Match(_mainViewModel.ActiveWindow, EditingRules);
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

    // --- Layer-view hotkeys (per profile) ------------------------------
    //
    // Per-keyboard table mapping a global hotkey to a layer index. Edited
    // here, committed on window close, and applied by
    // <see cref="HotkeyLayerViewService"/>. Each row carries its
    // registration outcome so the UI can show inline conflict warnings.

    /// <summary>Editable list of layer-view hotkey rows for the active profile. Seeded in the constructor; committed on close.</summary>
    public ObservableCollection<LayerViewHotkeyRow> EditingLayerViewHotkeys { get; } = new();

    /// <summary>True when the layer-view hotkey list is empty — drives the empty-state hint.</summary>
    public bool HasNoEditingLayerViewHotkeys => EditingLayerViewHotkeys.Count == 0;

    /// <summary>Convenience: any of the active bindings failed to register.</summary>
    public bool AnyLayerViewHotkeyFailed
    {
        get
        {
            foreach (var row in EditingLayerViewHotkeys)
                if (row.HasError) return true;
            return false;
        }
    }

    private void OnLayerViewHotkeysChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasNoEditingLayerViewHotkeys));
        OnPropertyChanged(nameof(AnyLayerViewHotkeyFailed));
    }

    /// <summary>F-key picker choices for the "new hotkey" row. Same list as the global show/hide picker (F13–F24 first, then F1–F12).</summary>
    public IReadOnlyList<string> LayerHotkeyKeyChoices => HotkeyKeyChoices;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLayerViewHotkeyCommand))]
    private string? _newLayerHotkeyKey;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddLayerViewHotkeyCommand))]
    private LayerViewModel? _newLayerHotkeyLayer;

    private bool CanAddLayerViewHotkey() =>
        !string.IsNullOrWhiteSpace(NewLayerHotkeyKey) && NewLayerHotkeyLayer is not null;

    [RelayCommand(CanExecute = nameof(CanAddLayerViewHotkey))]
    private void AddLayerViewHotkey()
    {
        if (NewLayerHotkeyLayer is null || string.IsNullOrWhiteSpace(NewLayerHotkeyKey)) return;
        var key = NewLayerHotkeyKey!;
        const string modifiers = "None";
        // Upsert by (key, modifiers) so re-picking the same hotkey for a
        // different layer updates in place instead of creating a duplicate
        // that would race the registry.
        for (var i = 0; i < EditingLayerViewHotkeys.Count; i++)
        {
            var existing = EditingLayerViewHotkeys[i].Binding;
            if (existing.KeyName.Equals(key, StringComparison.OrdinalIgnoreCase)
                && existing.ModifiersName.Equals(modifiers, StringComparison.OrdinalIgnoreCase))
            {
                if (existing.LayerIndex != NewLayerHotkeyLayer.Index)
                    EditingLayerViewHotkeys[i] = new LayerViewHotkeyRow(
                        new HotkeyLayerBinding(key, modifiers, NewLayerHotkeyLayer.Index));
                NewLayerHotkeyKey = null;
                return;
            }
        }
        EditingLayerViewHotkeys.Add(new LayerViewHotkeyRow(
            new HotkeyLayerBinding(key, modifiers, NewLayerHotkeyLayer.Index)));
        NewLayerHotkeyKey = null;
    }

    [RelayCommand]
    private void RemoveLayerViewHotkey(LayerViewHotkeyRow? row)
    {
        if (row is null) return;
        EditingLayerViewHotkeys.Remove(row);
    }

    /// <summary>
    /// Persists the edit buffer for the active profile and asks the host to
    /// re-bind. Called by <see cref="Views.SettingsWindow"/> on close so
    /// edits are batched (matches the AppLayerRules commit-on-close UX).
    /// </summary>
    public void CommitLayerViewHotkeys()
    {
        var snapshot = EditingLayerViewHotkeys.Select(r => r.Binding).ToList();
        var s = _settingsService.Load();
        var profileId = _mainViewModel.SelectedKeyboard.Id;
        var dict = new Dictionary<string, List<HotkeyLayerBinding>>(s.LayerViewHotkeys)
        {
            [profileId] = snapshot,
        };
        _settingsService.Save(s with { LayerViewHotkeys = dict });
        _mainViewModel.NotifyLayerViewHotkeysChanged();
        // The applier pushes results back synchronously via SetLayerViewHotkeyResults
        // before NotifyLayerViewHotkeysChanged returns, so the freshly populated
        // results are already visible here.
        SyncLayerViewHotkeyErrors();
    }

    /// <summary>
    /// Aligns each row's error message with the latest
    /// <c>HotkeyLayerViewService.ApplyBindings</c> outcome. Matches by
    /// binding value-equality (records). Called on construction and whenever
    /// <see cref="MainWindowViewModel.LayerViewHotkeyResults"/> changes.
    /// </summary>
    private void SyncLayerViewHotkeyErrors()
    {
        var results = _mainViewModel.LayerViewHotkeyResults;
        foreach (var row in EditingLayerViewHotkeys)
        {
            string? error = null;
            foreach (var r in results)
            {
                if (r.Binding == row.Binding)
                {
                    error = r.Success ? null : r.ErrorMessage;
                    break;
                }
            }
            row.ErrorMessage = error;
        }
        OnPropertyChanged(nameof(AnyLayerViewHotkeyFailed));
    }

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
        EditingRules.CollectionChanged -= OnEditingRulesChanged;
        EditingLayerViewHotkeys.CollectionChanged -= OnLayerViewHotkeysChanged;
    }

    /// <summary>Formatted percentage label next to the opacity slider. Reads
    /// straight off the main VM — re-raised by <see cref="OnMainPropertyChanged"/>
    /// when BackgroundOpacity changes.</summary>
    public string BackgroundOpacityPercent => $"{(int)(_mainViewModel.BackgroundOpacity * 100)}%";

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

    /// <summary>F-key choices offered for the global hotkey. Same rationale as the layer-key list (F13–F24 first, then F1–F12).</summary>
    public static IReadOnlyList<string> HotkeyKeyChoices { get; } =
        Enumerable.Range(13, 12).Select(n => $"F{n}")
            .Concat(Enumerable.Range(1, 12).Select(n => $"F{n}"))
            .ToArray();

    /// <summary>Shortcut to <see cref="PlatformCapabilities.IsGlobalHotkeySupported"/>
    /// for XAML bindings on the General tab.</summary>
    public static bool IsGlobalHotkeySupported => PlatformCapabilities.IsGlobalHotkeySupported;

    /// <summary>Top-hand picker choices for stacked layout. Mirrors svalboard's UX.</summary>
    public IReadOnlyList<string> AvailableTopHands { get; } = new[] { "Left", "Right" };

    /// <summary>Per-layer rows for the Layers tab: each row has a color picker.</summary>
    public ObservableCollection<LayerSettingsEntry> LayerEntries { get; } = new();

    /// <summary>"Layers — {profile name}" — section heading scoped to active keyboard.</summary>
    [ObservableProperty]
    private string _layersHeader = "";

    /// <summary>True iff a layout is loaded — toggles between the entries list and the placeholder hint.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsLayersHint))]
    private bool _hasLayoutForLayers;

    /// <summary>Inverse of <see cref="HasLayoutForLayers"/> for XAML visibility (Avalonia's compiled-binding `!` is finicky in non-trivial templates).</summary>
    public bool ShowsLayersHint => !HasLayoutForLayers;

    // Dispatch table for re-raising derived/transforming SettingsVM
    // properties when their underlying MainWindowViewModel properties
    // change. Pass-through properties (BackgroundOpacity, HotkeyKey,
    // IsStackedLayout, StackedTopHand, IsAutoSwitchKeyboardLayerEnabled,
    // Layers) are bound straight to MainViewModel in XAML and need no
    // entry here. Only properties this VM still owns — formatted labels,
    // radio booleans over an enum, per-profile rebuilds — appear below.
    private static readonly Dictionary<string, Action<SettingsViewModel>> _mainPropagators = new()
    {
        [nameof(MainWindowViewModel.BackgroundOpacity)] = s => s.OnPropertyChanged(nameof(BackgroundOpacityPercent)),
        [nameof(MainWindowViewModel.PressHighlightColor)] = s =>
        {
            s.OnPropertyChanged(nameof(PressHighlightColor));
            s.OnPropertyChanged(nameof(PressHighlightColorHex));
        },
        [nameof(MainWindowViewModel.SelectedKeyboard)] = s =>
        {
            s.RebuildLayerEntries();
            s.OnPropertyChanged(nameof(ActiveKeyboardProfile));
            s.OnPropertyChanged(nameof(ExitTapKey));
            s.OnPropertyChanged(nameof(ExitTapSummary));
            s.OnPropertyChanged(nameof(HasExitTap));
        },
        [nameof(MainWindowViewModel.LayerSourceHint)] = s => s.OnPropertyChanged(nameof(LayerSourceStatus)),
        // ActiveWindow (not MatchedAppLayerRule) drives the WouldFire preview now —
        // the preview is computed locally against the edit buffer, so the engine's
        // cached match is the wrong signal.
        [nameof(MainWindowViewModel.ActiveWindow)] = s => s.OnPropertyChanged(nameof(WouldFireText)),
        [nameof(MainWindowViewModel.AutoSwitchFallbackMode)] = s =>
        {
            s.OnPropertyChanged(nameof(IsFallbackPrevious));
            s.OnPropertyChanged(nameof(IsFallbackBase));
        },
        [nameof(MainWindowViewModel.ExitTapKey)] = s =>
        {
            s.OnPropertyChanged(nameof(ExitTapKey));
            s.OnPropertyChanged(nameof(ExitTapSummary));
            s.OnPropertyChanged(nameof(HasExitTap));
        },
        [nameof(MainWindowViewModel.LayerViewHotkeyResults)] = s => s.SyncLayerViewHotkeyErrors(),
    };

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is { } name && _mainPropagators.TryGetValue(name, out var propagate))
            propagate(this);
    }

    private void OnLayersCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A keymap reload renames or renumbers layers; rule-row layer
        // labels re-evaluate automatically via the MultiBinding's Count
        // trigger in XAML, so only the per-layer settings rows need to
        // rebuild here.
        RebuildLayerEntries();
    }

    /// <summary>
    /// Rebuilds the per-layer rows for the Layers tab. Each row wraps a
    /// <see cref="LayerColorEntry"/> (color picker + reset). Cheap to rebuild
    /// — layer counts are small, and entries are recreated rather than reused
    /// so the swatch colors stay in sync after Reset.
    /// </summary>
    private void RebuildLayerEntries()
    {
        LayerEntries.Clear();
        var profile = _mainViewModel.SelectedKeyboard;
        var profileName = profile?.DisplayName ?? "";
        LayersHeader = string.Format(Loc.Instance["Settings_LayersFormat"], profileName);
        HasLayoutForLayers = _mainViewModel.Layers.Count > 0;
        if (!HasLayoutForLayers || profile is null) return;

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

            LayerEntries.Add(new LayerSettingsEntry(colorEntry));
        }
    }

    // ── Version & Update Check ────────────────────────────────────────────
    // Implementation lives in UpdateChecker; these facade properties keep
    // existing XAML bindings (AppVersion, UpdateMessage, IsCheckingForUpdates,
    // CheckForUpdatesCommand) and the OnUpdateLinkClick handler unchanged.

    public UpdateChecker UpdateChecker { get; } = new();

    public string AppVersion => UpdateChecker.AppVersion;
    public string? UpdateMessage => UpdateChecker.UpdateMessage;
    public bool IsCheckingForUpdates => UpdateChecker.IsChecking;
    public string? UpdateUrl => UpdateChecker.UpdateUrl;

    [RelayCommand]
    private Task CheckForUpdatesAsync() => UpdateChecker.CheckAsync();
}

/// <summary>
/// Row wrapper for the layer-view hotkey list. Carries the
/// <see cref="HotkeyLayerBinding"/> being edited plus the latest
/// registration outcome so the Settings UI can show inline conflicts
/// (key already owned by another app, unsupported name).
/// </summary>
public sealed partial class LayerViewHotkeyRow : ObservableObject
{
    public HotkeyLayerBinding Binding { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _errorMessage;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public LayerViewHotkeyRow(HotkeyLayerBinding binding)
    {
        Binding = binding;
    }
}
