using System;
using System.IO;
using System.Linq;

namespace SmartCorral.Services;

/// <summary>
/// Minimal thread-safe file logger. Writes timestamped lines to data/smartcorral.log (portable, next
/// to the EXE) so the user (and we) can see what the app did — especially why an AI run did nothing
/// (the pipeline used to swallow LLM exceptions silently). Toggle via <see cref="Enabled"/>, wired to
/// AppSettings.EnableLogging (default on). NEVER throws — logging must not crash the app.
/// </summary>
public static class Logger
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string LogFile = Path.Combine(DataDir, "smartcorral.log");
    private static readonly object Gate = new();

    /// <summary>Master switch (set from settings at startup and when settings change). Default on.</summary>
    public static bool Enabled = true;

    public static void Info(string msg) => Write("INFO ", msg);

    public static void Warn(string msg) => Write("WARN ", msg);

    public static void Error(string msg, Exception? ex = null)
        => Write("ERROR", ex is null ? msg : $"{msg}  ->  {ex.GetType().Name}: {ex.Message}");

    /// <summary>Call once at startup to cap growth: if the log exceeds 1 MB, keep only the last ~2000 lines.</summary>
    public static void TrimIfNeeded()
    {
        try
        {
            if (!File.Exists(LogFile)) return;
            if (new FileInfo(LogFile).Length < 1_000_000) return;
            var tail = File.ReadLines(LogFile).TakeLast(2000).ToList();
            File.WriteAllLines(LogFile, tail);
        }
        catch { /* best-effort */ }
    }

    private static void Write(string level, string msg)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(DataDir);
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}");
            }
        }
        catch { /* never let logging crash the app */ }
    }
}
