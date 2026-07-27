using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SmartCorral.Services;

/// <summary>
/// Extracts an icon for a file/folder, cached by target path. Uses SHGetFileInfo (the reliable
/// Win32 icon API) so folders get the proper folder icon — Icon.ExtractAssociatedIcon does not.
/// </summary>
public static class IconService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

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

    public static ImageSource GetIcon(string? targetPath)
    {
        string key = targetPath ?? string.Empty;
        if (_cache.TryGetValue(key, out var cached)) return cached;

        ImageSource img = Load(targetPath);
        img.Freeze();
        _cache[key] = img;
        return img;
    }

    private static ImageSource Load(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
                return Fallback();

            var shfi = new SHFILEINFO();
            IntPtr h = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON);
            if (h != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(shfi.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                DestroyIcon(shfi.hIcon);
                return bmp;
            }
            return Fallback();
        }
        catch
        {
            return Fallback();
        }
    }

    private static ImageSource Fallback()
    {
        // transparent (not black) so a missed icon is invisible rather than an ugly box
        return new WriteableBitmap(32, 32, 96, 96, PixelFormats.Pbgra32, null);
    }
}
