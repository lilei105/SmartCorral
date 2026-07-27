using System;
using System.Collections.Generic;
using System.IO;
using SmartCorral.Services.Ai;

namespace SmartCorral.Services;

/// <summary>Enumerates the user's real Desktop — files AND folders (no COM, off-thread safe).</summary>
public static class DesktopScanner
{
    public static List<FileDescriptor> Scan()
    {
        var result = new List<FileDescriptor>();
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        foreach (string file in Directory.EnumerateFiles(desktop))
        {
            if (IsHidden(file)) continue;
            string name = Path.GetFileNameWithoutExtension(file);
            string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
            result.Add(new FileDescriptor(file, name, ext, IsFolder: false));
        }

        foreach (string dir in Directory.EnumerateDirectories(desktop))
        {
            if (IsHidden(dir)) continue;
            string name = Path.GetFileName(dir); // folders: keep the full name
            result.Add(new FileDescriptor(dir, name, Ext: "", IsFolder: true));
        }

        return result;
    }

    private static bool IsHidden(string path)
    {
        try { return (File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.System)) != 0; }
        catch { return false; }
    }
}
