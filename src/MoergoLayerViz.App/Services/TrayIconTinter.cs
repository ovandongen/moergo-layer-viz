using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Produces tinted variants of the tray icon for the active-layer color
/// indicator. The source PNG is loaded once at construction; per-color
/// renders are cached so re-tinting on layer changes is a dictionary hit.
///
/// Implementation: a <see cref="RenderTargetBitmap"/> draws a solid layer
/// color through the source icon's alpha channel as an opacity mask. This
/// avoids any assumption about the source PNG's internal pixel layout
/// (an earlier attempt to walk a pinned BGRA buffer produced a blank icon
/// because the decoded source wasn't in the format that path assumed).
/// </summary>
internal sealed class TrayIconTinter
{
    private readonly Bitmap _source;
    private readonly WindowIcon _original;
    private readonly Dictionary<uint, WindowIcon> _cache = new();

    public TrayIconTinter()
    {
        using var stream = AssetLoader.Open(new Uri("avares://MoergoLayerViz.App/Assets/icon.png"));
        _source = new Bitmap(stream);
        _original = new WindowIcon(_source);
    }

    public WindowIcon GetOriginal() => _original;

    /// <summary>
    /// Returns a tinted icon where every non-transparent pixel takes
    /// <paramref name="hexRgb"/> as its color, with source alpha preserved
    /// (edge anti-aliasing still reads correctly). Falls back to the
    /// untinted original on a parse failure.
    /// </summary>
    public WindowIcon GetTinted(string hexRgb)
    {
        if (!TryParseHexRgb(hexRgb, out var packed))
            return _original;
        if (_cache.TryGetValue(packed, out var cached))
            return cached;

        var icon = new WindowIcon(Render(packed));
        _cache[packed] = icon;
        return icon;
    }

    private RenderTargetBitmap Render(uint targetRgb)
    {
        var color = Color.FromArgb(
            0xFF,
            (byte)((targetRgb >> 16) & 0xFF),
            (byte)((targetRgb >> 8) & 0xFF),
            (byte)(targetRgb & 0xFF));

        var rtb = new RenderTargetBitmap(_source.PixelSize, _source.Dpi);
        var rect = new Rect(0, 0, _source.Size.Width, _source.Size.Height);

        using var ctx = rtb.CreateDrawingContext();
        var mask = new ImageBrush(_source) { Stretch = Stretch.Fill };
        using (ctx.PushOpacityMask(mask, rect))
        {
            ctx.DrawRectangle(new SolidColorBrush(color), null, rect);
        }
        return rtb;
    }

    private static bool TryParseHexRgb(string? hex, out uint packed)
    {
        packed = 0;
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.AsSpan().TrimStart('#');
        if (s.Length == 8) s = s[..6];
        if (s.Length != 6) return false;
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out packed);
    }
}
