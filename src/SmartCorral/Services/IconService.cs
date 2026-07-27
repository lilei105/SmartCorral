using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartCorral.Services;

/// <summary>
/// Extracts an icon for a file/folder/shortcut, cached by target path. (Phase 1a: simple cache.)
/// </summary>
public static class IconService
{
    private static readonly Dictionary<string, ImageSource> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource GetIcon(string? targetPath)
    {
        string key = targetPath ?? string.Empty;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        ImageSource img = Load(targetPath);
        img.Freeze(); // cross-thread safe
        _cache[key] = img;
        return img;
    }

    private static ImageSource Load(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return Fallback();

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon == null) return Fallback();

            return Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch
        {
            return Fallback();
        }
    }

    private static ImageSource Fallback()
    {
        var bmp = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgr32, null);
        return bmp;
    }
}
