using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SmartCorral.Services.Com;

namespace SmartCorral.Services;

/// <summary>
/// Extracts icons for items, cached by path. For shortcuts (.lnk) it reads the IconLocation and
/// fetches the icon from the actual source (target / .ico / .exe) so the shortcut ARROW overlay
/// is NOT included. Uses SHGetFileInfo (folders + default icons) and ExtractIconEx (specific index).
/// </summary>
public static class IconService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;

    private static readonly Dictionary<string, ImageSource> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icon for a shortcut file (.lnk/.url).
    /// showArrow: include the shortcut overlay arrow (when the ORIGINAL desktop item was a .lnk);
    /// suppress for raw files/folders we wrapped in a .lnk.</summary>
    public static ImageSource GetIconForShortcutFile(string absPath, bool showArrow)
    {
        string key = absPath + "|" + showArrow;
        if (_cache.TryGetValue(key, out var cached)) return cached;
        ImageSource img;
        if (absPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            img = showArrow ? Load(absPath) : LoadFromLnk(absPath);
        else
            img = Load(absPath); // .url etc. → SHGetFileInfo (no arrow overlay)
        img.Freeze();
        _cache[key] = img;
        return img;
    }

    private static ImageSource LoadFromLnk(string absLnk)
    {
        try
        {
            var (src, idx, target) = ShortcutService.ResolveIconLocation(absLnk);
            string iconSource = string.IsNullOrEmpty(src) ? target : src;

            if (!string.IsNullOrEmpty(iconSource) && (File.Exists(iconSource) || Directory.Exists(iconSource)))
            {
                IntPtr h = idx == 0 ? HIconByShGetFileInfo(iconSource) : HIconByExtract(iconSource, idx);
                if (h != IntPtr.Zero) return BitmapFromHIcon(h);
            }

            // fallback: target's icon, then the .lnk itself (with arrow), then transparent
            if (!string.IsNullOrEmpty(target))
            {
                IntPtr h2 = HIconByShGetFileInfo(target);
                if (h2 != IntPtr.Zero) return BitmapFromHIcon(h2);
            }
            return Load(absLnk);
        }
        catch
        {
            return Fallback();
        }
    }

    private static ImageSource Load(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return Fallback();

            IntPtr h = HIconByShGetFileInfo(path);
            return h != IntPtr.Zero ? BitmapFromHIcon(h) : Fallback();
        }
        catch
        {
            return Fallback();
        }
    }

    private static IntPtr HIconByShGetFileInfo(string path)
    {
        var shfi = new SHFILEINFO();
        SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
        return shfi.hIcon;
    }

    private static IntPtr HIconByExtract(string path, int index)
    {
        ExtractIconEx(path, index, out IntPtr large, out _, 1);
        return large;
    }

    private static ImageSource BitmapFromHIcon(IntPtr hIcon)
    {
        var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        DestroyIcon(hIcon);
        return bmp;
    }

    private static ImageSource Fallback()
    {
        // transparent (not black) so a missed icon is invisible rather than an ugly box
        return new WriteableBitmap(32, 32, 96, 96, PixelFormats.Pbgra32, null);
    }
}
