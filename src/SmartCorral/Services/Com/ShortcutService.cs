using System;
using System.IO;

namespace SmartCorral.Services.Com;

/// <summary>
/// Creates / resolves .lnk shortcuts via the WScript.Shell COM object — late-bound (dynamic),
/// so no COM reference is needed and `dotnet build` stays clean. Shortcuts live under data/shortcuts/.
/// </summary>
public static class ShortcutService
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string ShortcutsDir = Path.Combine(DataDir, "shortcuts");

    /// <summary>Creates a .lnk pointing to targetPath. Returns the path relative to the data folder ("shortcuts/...").</summary>
    public static string CreateShortcut(string targetPath, string displayName)
    {
        Directory.CreateDirectory(ShortcutsDir);
        string fileName = MakeUnique(displayName + ".lnk");
        string abs = Path.Combine(ShortcutsDir, fileName);

        Type shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM object unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(abs);
        shortcut.TargetPath = targetPath;
        if (Directory.Exists(targetPath)) shortcut.WorkingDirectory = targetPath;
        shortcut.Save();

        return Path.Combine("shortcuts", fileName);
    }

    /// <summary>Resolves a relative shortcut path to its target, or string.Empty on failure.</summary>
    public static string Resolve(string relativeShortcut)
    {
        try
        {
            string abs = Path.Combine(DataDir, relativeShortcut);
            if (!File.Exists(abs)) return string.Empty;

            Type shellType = Type.GetTypeFromProgID("WScript.Shell")!;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(abs);
            return (string)shortcut.TargetPath;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string MakeUnique(string fileName)
    {
        string candidate = fileName;
        int i = 1;
        while (File.Exists(Path.Combine(ShortcutsDir, candidate)))
        {
            string stem = Path.GetFileNameWithoutExtension(fileName);
            candidate = $"{stem} ({i}).lnk";
            i++;
        }
        return candidate;
    }
}
