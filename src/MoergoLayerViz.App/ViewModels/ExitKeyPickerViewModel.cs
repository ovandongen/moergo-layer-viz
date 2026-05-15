using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.Core.Layout;

namespace MoergoLayerViz.App.ViewModels;

/// <summary>
/// View model for the exit-tap key picker popup. Hosts a stripped-down
/// copy of the active keyboard profile (empty labels, no combo earmarks)
/// and lets the user click a single cap to choose the firmware key index
/// that will toggle the auto-switch engine's escape hatch on double-tap.
///
/// <para>Implements <see cref="IBoardSurface"/> so <c>BoardView</c> can
/// render it without forking the control — the same canvas math, palette,
/// and per-cap template apply. The selected cap is tinted green via
/// <see cref="KeyViewModel.KeyFillColor"/>.</para>
/// </summary>
public partial class ExitKeyPickerViewModel : ObservableObject, IBoardSurface
{
    private const string DefaultFill = AppTheme.KeyDefaultFillHex;
    private const string SelectedFill = AppTheme.AccentGreenHex;

    private readonly IKeyboardProfile _profile;
    private int? _selectedIndex;

    public ExitKeyPickerViewModel(IKeyboardProfile profile, int? initialSelection)
    {
        _profile = profile;

        foreach (var pos in _profile.Keys)
        {
            // Stripped KeyViewModel: empty Label / Subscript / Icon /
            // TopLeftLabel so the picker shows the physical layout only,
            // no semantic clutter. KeyFillColor flips between default and
            // SelectedFill as the user toggles a cap.
            var vm = new KeyViewModel(pos)
            {
                Label = "",
                Subscript = "",
                IconName = "",
                TopLeftLabel = "",
                Behavior = "",
                KeyFillColor = DefaultFill,
                IsInCombo = false,
                IsPressed = false,
                Tooltip = pos.Description ?? $"#{pos.Index}",
            };
            Keys.Add(vm);
            (pos.Hand == Hand.Left ? LeftKeys : RightKeys).Add(vm);
        }

        // Apply the initial selection so reopening the picker shows the
        // current configuration. Silently drops an out-of-range index that
        // wouldn't map to a cap on this profile (e.g. carried over from a
        // different keyboard).
        if (initialSelection is int idx && idx >= 0 && idx < Keys.Count)
        {
            _selectedIndex = idx;
            Keys[idx].KeyFillColor = SelectedFill;
        }

        UpdateChips();
    }

    public ObservableCollection<KeyViewModel> Keys { get; } = new();
    public ObservableCollection<KeyViewModel> LeftKeys { get; } = new();
    public ObservableCollection<KeyViewModel> RightKeys { get; } = new();

    // IBoardSurface — picker always renders in horizontal (non-stacked) mode
    // so the hand offsets are zero and the canvas size matches the profile.
    public double CanvasWidth => _profile.CanvasWidth;
    public double CanvasHeight => _profile.CanvasHeight;
    public double BoardSurfaceWidth => _profile.CanvasWidth;
    public double BoardSurfaceHeight => _profile.CanvasHeight;
    public double LeftHandX => 0;
    public double LeftHandY => 0;
    public double RightHandX => 0;
    public double RightHandY => 0;

    // Press dots aren't pulsed in the picker (IsPressed is never set), but
    // BoardView binds these regardless so the brush parses cleanly.
    public string PressHighlightColor => AppTheme.PressHighlightDefaultHex;
    public string PressHighlightStrokeColor => AppTheme.PickerPressStrokeHex;

    public Action<int>? OnKeyTapped => ToggleKey;

    /// <summary>True when the user has at least one cap selected — drives
    /// the OK button's IsEnabled and the empty-state hint visibility.</summary>
    [ObservableProperty]
    private bool _hasSelection;

    /// <summary>Human-readable summary of the current selection, e.g.
    /// "#42". Empty when nothing's picked.</summary>
    [ObservableProperty]
    private string _selectionSummary = "";

    /// <summary>Snapshot of the currently-selected firmware key index.
    /// Consumed by the SettingsViewModel when the user confirms the
    /// picker. Null when the user picked nothing.</summary>
    public int? SelectedIndex => _selectedIndex;

    private void ToggleKey(int index)
    {
        if (index < 0 || index >= Keys.Count) return;

        // Tap an already-selected key to deselect.
        if (_selectedIndex == index)
        {
            Keys[index].KeyFillColor = DefaultFill;
            _selectedIndex = null;
            UpdateChips();
            return;
        }

        // Replace the previous selection (single-key picker).
        if (_selectedIndex is int prev && prev >= 0 && prev < Keys.Count)
            Keys[prev].KeyFillColor = DefaultFill;

        _selectedIndex = index;
        Keys[index].KeyFillColor = SelectedFill;
        UpdateChips();
    }

    [RelayCommand]
    private void ClearSelection()
    {
        if (_selectedIndex is int idx && idx >= 0 && idx < Keys.Count)
            Keys[idx].KeyFillColor = DefaultFill;
        _selectedIndex = null;
        UpdateChips();
    }

    private void UpdateChips()
    {
        HasSelection = _selectedIndex is not null;
        SelectionSummary = _selectedIndex switch
        {
            int i when i >= 0 && i < _profile.Keys.Count =>
                _profile.Keys[i].Description is { Length: > 0 } d ? $"{d} (#{i})" : $"#{i}",
            _ => "",
        };
    }
}
