namespace MoergoLayerViz.App.Services;

/// <summary>
/// Single source of truth for hard-coded UI colors. Mirrored as
/// <c>SolidColorBrush</c> resources in <c>App.axaml</c> keyed by the same
/// name (without the <c>Hex</c> suffix) so XAML consumers can write
/// <c>{StaticResource Text}</c> and C# consumers can write
/// <c>AppTheme.TextHex</c> — keep both in sync when adding a new color.
/// <para>
/// The "rule" (per user preference): every literal <c>#RRGGBB</c> outside this
/// file should be considered a bug — add a name here and a matching resource
/// in <c>App.axaml</c> instead.
/// </para>
/// </summary>
internal static class AppTheme
{
    // ─── Backgrounds (Catppuccin Mocha base) ────────────────────────────
    public const string BgCrustHex = "#181825";        // deepest — board background, tab background base
    public const string BgBaseHex = "#1E1E2E";         // window background, key label
    public const string BgSurface0Hex = "#313244";     // separator lines, panel borders
    public const string BgSurface1Hex = "#45475A";     // swatch border

    // ─── Text (Catppuccin) ───────────────────────────────────────────────
    public const string TextHex = "#CDD6F4";           // primary text
    public const string TextSubtleHex = "#A6ADC8";     // secondary text
    public const string TextMutedHex = "#6C7086";      // hints, labels
    public const string TextStatusBarHex = "#8A8AA0";  // status-bar key-count fragments
    public const string TextAccessibilityBodyHex = "#D0D0D8";

    // ─── Accents (Catppuccin) ────────────────────────────────────────────
    public const string AccentBlueHex = "#89B4FA";
    public const string AccentGreenHex = "#A6E3A1";    // also picker-selected key fill
    public const string AccentTealHex = "#94E2D5";
    public const string AccentYellowHex = "#F9E2AF";
    public const string AccentRedHex = "#F38BA8";

    // ─── Tab chrome (custom, sister-app port) ───────────────────────────
    public const string TabBgHex = "#363649";
    public const string TabBorderHex = "#4A4A63";
    public const string TabHoverBgHex = "#3E3E5C";
    public const string TabHoverBorderHex = "#6A6A88"; // also used as status-bar divider
    public const string TabSelectedBgHex = "#46466A";
    public const string TabSelectedBorderHex = "#7A7A9A";

    // ─── Key board ──────────────────────────────────────────────────────
    public const string KeyDefaultFillHex = "#F2F2F2"; // off-white cap; also bright foreground on dark fills
    public const string KeyStrokeHex = "#B0B0B0";
    public const string KeyLayerFillHex = "#1F8B4C";   // fallback green for layer-target caps

    // ─── Press highlight (default) ──────────────────────────────────────
    public const string PressHighlightDefaultHex = "#FFD60A";       // user-customizable; this is the seed
    public const string PressHighlightStrokeFallbackHex = "#8A6D00"; // used only if PressHighlightColor can't be parsed
    public const string PickerPressStrokeHex = "#7A6803";

    // ─── Combo indicator (decorative triangle on key) ───────────────────
    public const string ComboFillHex = "#4DABF7";
    public const string ComboStrokeHex = "#1971C2";

    // ─── Error toast ────────────────────────────────────────────────────
    public const string ErrorBgHex = "#E03131";
    public const string ErrorBorderHex = "#A51F1F";
    public const string ErrorTextHex = "#FFFFFF";
}
