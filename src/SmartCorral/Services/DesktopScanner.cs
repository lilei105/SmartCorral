using System;
using System.Collections.Generic;
using System.IO;
using SmartCorral.Services.Ai;

namespace SmartCorral.Services;

/// <summary>Enumerates the user's real Desktop files (no COM — just paths, safe off-thread).</summary>
public static class DesktopScanner
{
    public static List<FileDescriptor> ScanFiles()
    {
        var result = new List<FileDescriptor>();
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);

        foreach (string file in Directory.EnumerateFiles(desktop))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            string ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
            result.Add(new FileDescriptor(file, name, ext));
        }

        return result;
    }
}
