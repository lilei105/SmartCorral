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
/// Icons are pulled at the largest available size (jumbo 256 → extralarge 48 → large 32) so they
/// stay crisp on high-DPI monitors (a PerMonitorV2 app downscales them at render time).
/// </summary>
public static class IconService
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    [DllImport("shell32.dll")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

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
    private const uint SHGFI_SYSICONINDEX = 0x000000400;

    // System image list sizes (same indices across all of them, different resolutions).
    private const int SHIL_LARGE = 0x0;
    private const int SHIL_EXTRALARGE = 0x2;
    private const int SHIL_JUMBO = 0x4;
    private const int ILD_NORMAL = 0x00000000;

    private static readonly Guid IID_IImageList = new("46EB5926-582E-4017-9FDF-E899822AA095");

    /// <summary>IImageList COM interface — methods declared in vtable order; only GetIcon is used.</summary>
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E899822AA095")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        void Add(IntPtr hbmImage, IntPtr hbmMask, out int pi);
        void ReplaceIcon(int i, IntPtr hicon, out int pi);
        void SetOverlayImage(int iImage, int iOverlay);
        void Recreate(int cx, int cy, uint flags, uint initial, out int pi);
        void GetIconSize(out int cx, out int cy);
        void SetIconSize(int cx, int cy);
        void GetImageCount(out int pi);
        void SetBkColor(uint clrBk, out uint pclr);
        void GetBkColor(out uint pclr);
        void BeginDrag(int iTrack, int dxHotspot, int dyHotspot);
        void DragMove(int x, int y);
        void DragLeave(IntPtr hwnd);
        void DragEnter(IntPtr hwndLock, int x, int y);
        void EndDrag();
        void SetDragCursorImage(IntPtr himl, int iDrag, int dxHotspot, int dyHotspot);
        void GetDragImage(IntPtr ppt, IntPtr pptHotspot, ref Guid riid, out IntPtr ppv);
        void GetItemFlags(int i, out int dwFlags);
        void GetOverlayImage(int iOverlay, out int piIndex);
        void GetIcon(int i, int flags, out IntPtr picon);
    }

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
                IntPtr h = idx == 0 ? HIconForPath(iconSource) : HIconByExtract(iconSource, idx);
                if (h != IntPtr.Zero) return BitmapFromHIcon(h);
            }

            // fallback: target's icon, then the .lnk itself (with arrow), then transparent
            if (!string.IsNullOrEmpty(target))
            {
                IntPtr h2 = HIconForPath(target);
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

            IntPtr h = HIconForPath(path);
            return h != IntPtr.Zero ? BitmapFromHIcon(h) : Fallback();
        }
        catch
        {
            return Fallback();
        }
    }

    /// <summary>Best available icon for a shell path: jumbo(256) → extralarge(48) → large(32).</summary>
    private static IntPtr HIconForPath(string path)
    {
        int index = SysIconIndex(path);
        if (index >= 0)
        {
            IntPtr h = GetIconFromImageList(SHIL_JUMBO, index);
            if (h == IntPtr.Zero) h = GetIconFromImageList(SHIL_EXTRALARGE, index);
            if (h != IntPtr.Zero) return h;
        }
        return HIconByShGetFileInfo(path); // final fallback: 32px large icon
    }

    private static int SysIconIndex(string path)
    {
        var shfi = new SHFILEINFO();
        SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_SYSICONINDEX);
        return shfi.iIcon;
    }

    private static IntPtr GetIconFromImageList(int shil, int index)
    {
        Guid iid = IID_IImageList;
        int hr = SHGetImageList(shil, ref iid, out IntPtr punk);
        if (hr != 0 || punk == IntPtr.Zero) return IntPtr.Zero;

        object obj;
        try
        {
            obj = Marshal.GetObjectForIUnknown(punk);
        }
        catch
        {
            Marshal.Release(punk);
            return IntPtr.Zero;
        }
        Marshal.Release(punk); // drop the ref SHGetImageList added; the RCW holds its own

        try
        {
            ((IImageList)obj).GetIcon(index, ILD_NORMAL, out IntPtr hicon);
            return hicon; // caller must DestroyIcon
        }
        catch
        {
            return IntPtr.Zero;
        }
        finally
        {
            Marshal.ReleaseComObject(obj);
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
