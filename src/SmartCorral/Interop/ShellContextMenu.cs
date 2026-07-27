using System;
using System.IO;
using System.Runtime.InteropServices;
using WF = System.Windows.Forms;

namespace SmartCorral.Interop;

/// <summary>
/// Shows the real Windows shell context menu for a file/folder path.
/// COM: SHParseDisplayName → SHBindToParent → IShellFolder.GetUIObjectOf → IContextMenu →
/// QueryContextMenu → TrackPopupMenuEx → InvokeCommand.
/// Includes step logging to data/ctxmenu_debug.log for crash diagnosis.
/// </summary>
public static class ShellContextMenu
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string pszName, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint psfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(IntPtr pidl, [In] ref Guid riid, out IntPtr ppv, out IntPtr ppidlLast);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    // A hidden, NORMAL (non-NOACTIVATE) window that can be foregrounded so the menu dismisses
    // on click-away. TrackPopupMenuEx requires the owner to be in the foreground.
    private static WF.Form? _helper;
    private static IntPtr EnsureHelperHwnd()
    {
        if (_helper == null || _helper.IsDisposed)
        {
            _helper = new WF.Form
            {
                ShowInTaskbar = false,
                FormBorderStyle = WF.FormBorderStyle.FixedToolWindow,
                StartPosition = WF.FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000), // off-screen
                Size = new System.Drawing.Size(1, 1),
            };
            _helper.Show();
            _helper.Hide();
        }
        return _helper.Handle;
    }

    private const uint CMF_NORMAL = 0x00000000;
    private const uint TPM_RETURNCMD = 0x0100;
    private const uint TPM_LEFTALIGN = 0x0000;
    private const uint TPM_TOPALIGN = 0x0000;
    private const int SW_SHOWNORMAL = 1;
    private const uint FIRST_CMD = 1;
    private const uint LAST_CMD = 0x7FFF;
    private const uint CMIC_MASK_UNICODE = 0x00004000;

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    private static readonly string LogPath = Path.Combine(AppContext.BaseDirectory, "data", "ctxmenu_debug.log");

    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\n"); } catch { }
    }

    public static void Show(string filePath, IntPtr hwnd, int screenX, int screenY)
    {
        Log($"=== Show: file={filePath} hwnd={hwnd} x={screenX} y={screenY} ===");

        if (string.IsNullOrEmpty(filePath) || (!File.Exists(filePath) && !Directory.Exists(filePath)))
        {
            Log("file not found, abort");
            return;
        }

        IntPtr pidl = IntPtr.Zero;
        IntPtr hMenu = IntPtr.Zero;
        object? cmObj = null;
        object? parentObj = null;
        IntPtr parentPtr = IntPtr.Zero;
        IntPtr cmPtr = IntPtr.Zero;

        try
        {
            // Step 1: parse path → PIDL
            int hr = SHParseDisplayName(filePath, IntPtr.Zero, out pidl, 0, out _);
            Log($"SHParseDisplayName hr={hr} pidl={pidl}");
            if (hr != 0 || pidl == IntPtr.Zero) { Log("parse failed"); return; }

            // Step 2: bind to parent IShellFolder
            var iid = IID_IShellFolder;
            hr = SHBindToParent(pidl, ref iid, out parentPtr, out IntPtr childPIDL);
            Log($"SHBindToParent hr={hr} parent={parentPtr} childPIDL={childPIDL}");
            if (hr != 0 || parentPtr == IntPtr.Zero) { Log("bind failed"); return; }

            parentObj = Marshal.GetObjectForIUnknown(parentPtr);
            IShellFolder parent = (IShellFolder)parentObj;
            Log("got IShellFolder");

            // Step 3: get IContextMenu
            Guid cmGuid = IID_IContextMenu;
            uint prgf = 0;
            parent.GetUIObjectOf(IntPtr.Zero, 1, new[] { childPIDL }, ref cmGuid, ref prgf, out cmPtr);
            Log($"GetUIObjectOf cmPtr={cmPtr}");
            if (cmPtr == IntPtr.Zero) { Log("no IContextMenu"); return; }

            cmObj = Marshal.GetObjectForIUnknown(cmPtr);
            IContextMenu cm = (IContextMenu)cmObj;
            Log("got IContextMenu");

            // Step 4: build HMENU
            hMenu = CreatePopupMenu();
            hr = cm.QueryContextMenu(hMenu, 0, FIRST_CMD, LAST_CMD, CMF_NORMAL);
            Log($"QueryContextMenu hr={hr} hMenu={hMenu}");

            // Step 5: show (use a normal hidden helper window as owner so the menu dismisses on click-away)
            IntPtr menuHwnd = EnsureHelperHwnd();
            SetForegroundWindow(menuHwnd);
            uint cmd = TrackPopupMenuEx(hMenu, TPM_RETURNCMD | TPM_LEFTALIGN | TPM_TOPALIGN, screenX, screenY, menuHwnd, IntPtr.Zero);
            Log($"TrackPopupMenuEx cmd={cmd}");

            // Step 6: invoke
            if (cmd >= FIRST_CMD)
            {
                uint offset = cmd - FIRST_CMD;
                var info = new CMINVOKECOMMANDINFOEX
                {
                    cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFOEX>(),
                    fMask = CMIC_MASK_UNICODE,
                    hwnd = menuHwnd,
                    lpVerb = (IntPtr)offset,
                    lpVerbW = (IntPtr)offset,
                    nShow = SW_SHOWNORMAL
                };
                hr = cm.InvokeCommand(ref info);
                Log($"InvokeCommand hr={hr} offset={offset}");
            }
            else
            {
                Log("no command selected (cancelled)");
            }
        }
        catch (Exception ex)
        {
            Log($"EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Log(ex.StackTrace ?? "(no stack)");
        }
        finally
        {
            if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
            if (cmPtr != IntPtr.Zero) Marshal.Release(cmPtr);
            if (parentPtr != IntPtr.Zero) Marshal.Release(parentPtr);
            if (pidl != IntPtr.Zero) Marshal.FreeCoTaskMem(pidl);
            Log("cleanup done");
        }
    }

    // ---- COM interop ----

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    private interface IShellFolder
    {
        void ParseDisplayName(IntPtr hwnd, IntPtr pbc, [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName, out uint pchEaten, out IntPtr pidl, out uint pvResult);
        void EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);
        void BindToObject(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        void BindToStorage(IntPtr pidl, IntPtr pbc, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lparam, IntPtr pidl1, IntPtr pidl2);
        void CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
        void GetAttributesOf(uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] apidl, ref uint rgfInOut);
        void GetUIObjectOf(IntPtr hwndOwner, uint cidl, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] apidl, [In] ref Guid riid, ref uint prgf, out IntPtr ppv);
        void GetDisplayNameOf(IntPtr pidl, uint uFlags, out IntPtr pName);
        void SetNameOf(IntPtr hwnd, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] string pszName, uint uFlags, out IntPtr ppidlOut);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(ref CMINVOKECOMMANDINFOEX info);
        [PreserveSig] int GetCommandString(uint idCmd, uint uType, uint dwReserved, IntPtr pszName, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public int dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public uint dwHotKey2;
    }
}
