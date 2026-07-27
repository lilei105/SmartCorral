using System.IO;
using System.Text.Json;
using SmartCorral.Models;

namespace SmartCorral.Services;

/// <summary>
/// Loads/saves AppData to data/frames.json (portable, next to the EXE).
/// </summary>
public static class PersistenceService
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string FramesFile = Path.Combine(DataDir, "frames.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppData Load()
    {
        try
        {
            if (!File.Exists(FramesFile)) return new AppData();
            string json = File.ReadAllText(FramesFile);
            return JsonSerializer.Deserialize<AppData>(json, Options) ?? new AppData();
        }
        catch
        {
            // Corrupt or unreadable — start fresh. (TODO: log)
            return new AppData();
        }
    }

    public static void Save(AppData data)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            string json = JsonSerializer.Serialize(data, Options);
            File.WriteAllText(FramesFile, json);
        }
        catch
        {
            // TODO: log
        }
    }
}
