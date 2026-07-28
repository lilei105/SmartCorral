using System.IO;
using System.Security.Cryptography;
using System.Text;
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
    private static readonly string SettingsFile = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static AppSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return new AppSettings();
            string json = File.ReadAllText(SettingsFile);
            var s = JsonSerializer.Deserialize<AppSettings>(json, Options) ?? new AppSettings();
            s.AiApiKey = UnprotectKey(s.AiApiKey); // decrypt the API key (DPAPI, current user)
            return s;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(DataDir);
            // Encrypt the API key at rest (DPAPI, current user). Swap-and-restore so the in-memory
            // object keeps its plaintext value.
            var plain = settings.AiApiKey;
            try
            {
                settings.AiApiKey = ProtectKey(plain);
                File.WriteAllText(SettingsFile, JsonSerializer.Serialize(settings, Options));
            }
            finally
            {
                settings.AiApiKey = plain;
            }
        }
        catch
        {
            // TODO: log
        }
    }

    // ---- API-key protection (DPAPI) ----
    private const string DpapiPrefix = "dpapi:";

    private static string ProtectKey(string plain)
    {
        if (string.IsNullOrEmpty(plain)) return plain;
        try
        {
            byte[] cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
            return DpapiPrefix + Convert.ToBase64String(cipher);
        }
        catch
        {
            return plain; // best-effort: fall back to plaintext rather than losing the key
        }
    }

    private static string UnprotectKey(string stored)
    {
        if (string.IsNullOrEmpty(stored) || !stored.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            return stored; // legacy plaintext or empty — pass through
        try
        {
            byte[] cipher = Convert.FromBase64String(stored[DpapiPrefix.Length..]);
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return ""; // can't decrypt (different user/machine/corrupt) → blank key
        }
    }

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
