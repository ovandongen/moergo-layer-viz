using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MoergoLayerViz.Core.Colors;

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
    private readonly Dictionary<(byte r, byte g, byte b), WindowIcon> _cache = new();

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
        if (!HexRgb.TryParse(hexRgb, out var r, out var g, out var b))
            return _original;
        var key = (r, g, b);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var icon = new WindowIcon(Render(r, g, b));
        _cache[key] = icon;
        return icon;
    }

    private RenderTargetBitmap Render(byte r, byte g, byte b)
    {
        var color = Color.FromArgb(0xFF, r, g, b);

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
}
