using System;
using System.IO;

namespace SmartCorral.Services.Com;

/// <summary>
/// Imports dropped files as shortcuts under data/shortcuts/ via the WScript.Shell COM object
/// (late-bound/dynamic — no COM reference, dotnet-buildable).
///
/// A dropped .lnk/.url is COPIED verbatim so its target / working-dir / arguments / icon are preserved
/// (launching it then behaves exactly like double-clicking the original desktop shortcut — e.g. OBS
/// needs its "start in" folder to find its locale files).
/// A dropped raw file/folder gets a freshly-created .lnk pointing at it.
/// </summary>
public static class ShortcutService
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string ShortcutsDir = Path.Combine(DataDir, "shortcuts");

    /// <summary>Imports a dropped file as a shortcut under data/shortcuts/. Returns the path relative to data/.
    /// For a raw file/folder (not a .lnk/.url), the freshly-created wrapper .lnk points at
    /// <paramref name="targetOverride"/> when given (its custody path) instead of the original desktop
    /// path — so the shortcut keeps working once the original is moved into custody. .lnk/.url originals
    /// are copied verbatim (their own target is independent of custody), so the override is ignored.</summary>
    public static string Import(string sourcePath, string displayName, string? targetOverride = null)
    {
        Directory.CreateDirectory(ShortcutsDir);

        bool isShortcut = sourcePath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                       || sourcePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

        string ext = isShortcut ? Path.GetExtension(sourcePath) : ".lnk";
        string fileName = MakeUnique(displayName + ext);
        string abs = Path.Combine(ShortcutsDir, fileName);

        if (isShortcut)
        {
            // Preserve the original shortcut's target/working-dir/arguments/icon exactly.
            File.Copy(sourcePath, abs, overwrite: true);
        }
        else
        {
            CreateNewLnk(abs, string.IsNullOrEmpty(targetOverride) ? sourcePath : targetOverride);
        }

        return Path.Combine("shortcuts", fileName);
    }

    /// <summary>Re-points an existing wrapper .lnk at a new target (used at startup when RetakeAll moves
    /// a raw file/folder to a fresh custody path for this session). No-op for .url or missing files.</summary>
    public static void Retarget(string relativePath, string newTarget)
    {
        try
        {
            string abs = Path.Combine(DataDir, relativePath);
            if (!File.Exists(abs)) return;
            if (relativePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return;

            Type t = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(abs);
            sc.TargetPath = newTarget;
            if (Directory.Exists(newTarget)) sc.WorkingDirectory = newTarget;
            sc.Save();
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Resolves a shortcut to its target path (for icon extraction), or string.Empty.</summary>
    public static string ResolveTarget(string relativePath)
    {
        try
        {
            string abs = Path.Combine(DataDir, relativePath);
            if (!File.Exists(abs)) return string.Empty;
            if (relativePath.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) return abs;

            Type t = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(abs);
            return (string)sc.TargetPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>Absolute path for a shortcut stored relative to data/.</summary>
    public static string AbsolutePath(string relativePath) => Path.Combine(DataDir, relativePath);

    /// <summary>Reads a .lnk's IconLocation ("path,index" or "") and target. Used to fetch the icon
    /// WITHOUT the shortcut arrow overlay (which SHGetFileInfo(.lnk) would include).</summary>
    public static (string Source, int Index, string Target) ResolveIconLocation(string absLnkPath)
    {
        try
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(t)!;
            dynamic sc = shell.CreateShortcut(absLnkPath);
            string il = (string)sc.IconLocation;
            string target = (string)sc.TargetPath;

            string src = "";
            int idx = 0;
            if (!string.IsNullOrWhiteSpace(il))
            {
                int comma = il.LastIndexOf(',');
                if (comma >= 0 && int.TryParse(il.Substring(comma + 1).Trim(), out idx))
                    src = il.Substring(0, comma).Trim();
                else
                    src = il.Trim();
            }
            return (src, idx, target ?? string.Empty);
        }
        catch
        {
            return (string.Empty, 0, string.Empty);
        }
    }

    private static void CreateNewLnk(string absLnkPath, string targetPath)
    {
        Type t = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM object unavailable.");
        dynamic shell = Activator.CreateInstance(t)!;
        dynamic sc = shell.CreateShortcut(absLnkPath);
        sc.TargetPath = targetPath;
        if (Directory.Exists(targetPath)) sc.WorkingDirectory = targetPath;
        sc.Save();
    }

    private static string MakeUnique(string fileName)
    {
        string candidate = fileName;
        int i = 1;
        while (File.Exists(Path.Combine(ShortcutsDir, candidate)))
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            candidate = $"{stem} ({i}){Path.GetExtension(fileName)}";
            i++;
        }
        return candidate;
    }
}
