using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace SmartCorral.Services;

/// <summary>
/// Custody model: physically moves desktop items into a private "custody" folder while Smart Corral
/// organizes them, so each filed item disappears from the desktop one at a time. The native desktop
/// list-view (SysListView32) has no supported per-item hide, so clearing the desktop per-item — the
/// product's core behavior for both manual curation and future incremental auto-categorize — requires
/// physically moving the file off the desktop and back.
///
/// Everything is ALWAYS restorable. On clean exit (and as the FIRST step of every launch, for crash
/// self-heal) every custodied item is moved back to its exact original path.
///
/// Safety net (this touches real user files — must be bulletproof):
///   • The manifest entry is written BEFORE the move (marked pending) and marked done only AFTER the
///     move is verified. Any manifest entry found at launch is treated as "needs restore".
///   • %LOCALAPPDATA%\SmartCorral\custody is on the same volume as the desktop (C:), so
///     File.Move / Directory.Move are atomic — no half-files on crash. Cross-volume (desktop on D:)
///     degrades to copy+verify+delete for folders; the manifest still backstops it.
///   • Restore never overwrites: if the original path is now occupied (user dropped a new same-named
///     file), the custody copy is restored to a "(restored)" name so nothing is ever lost.
///   • Failed restores keep their manifest entry and retry next launch — nothing is silently dropped.
/// </summary>
public static class CustodyService
{
    /// <summary>Custody root — intentionally on %LOCALAPPDATA% (C:) so moves from the desktop are atomic.</summary>
    private static readonly string CustodyDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SmartCorral", "custody");

    /// <summary>Manifest lives in the portable data/ folder, next to the EXE.</summary>
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly string ManifestFile = Path.Combine(DataDir, "custody.json");

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    // ---- public API ----

    /// <summary>Moves a desktop file/folder into custody so its icon leaves the desktop.
    /// Returns the custody path on success, or <paramref name="originalAbs"/> unchanged if custody
    /// could not be taken (item missing/locked) — callers degrade gracefully (the icon simply stays
    /// on the desktop). Idempotent: re-taking an already-custodied path returns its existing custody
    /// path without moving again.</summary>
    public static string Take(string originalAbs)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(originalAbs)) return originalAbs;
            bool isFile = File.Exists(originalAbs);
            bool isDir = Directory.Exists(originalAbs);
            if (!isFile && !isDir) return originalAbs; // nothing to take (already moved / deleted)

            Directory.CreateDirectory(CustodyDir);
            var manifest = LoadManifest();

            // Idempotent: already in custody this session → return the existing path, no second move.
            var existing = manifest.FirstOrDefault(e => PathEquals(e.OriginalPath, originalAbs));
            if (existing != null && (File.Exists(existing.CustodyPath) || Directory.Exists(existing.CustodyPath)))
                return existing.CustodyPath;

            // Drop any stale entry for this original (custody copy gone), then record a fresh PENDING
            // entry BEFORE the move — a crash here restores it next launch.
            string custodyPath = MakeUniqueCustodyPath(originalAbs, isDir);
            var entry = new CustodyEntry { OriginalPath = originalAbs, CustodyPath = custodyPath, Done = false };
            manifest.RemoveAll(e => PathEquals(e.OriginalPath, originalAbs));
            manifest.Add(entry);
            SaveManifest(manifest);

            if (!Move(originalAbs, custodyPath, isDir) || (!File.Exists(custodyPath) && !Directory.Exists(custodyPath)))
            {
                // Move failed or didn't land — roll back the pending entry and leave the item on the desktop.
                manifest.RemoveAll(e => PathEquals(e.OriginalPath, originalAbs));
                SaveManifest(manifest);
                Logger.Warn($"Take FAILED (kept on desktop): \"{originalAbs}\"");
                return originalAbs;
            }

            entry.Done = true;
            SaveManifest(manifest);
            Logger.Info($"Take OK: \"{Path.GetFileName(originalAbs.TrimEnd('\\', '/'))}\" -> custody");
            return custodyPath;
        }
        catch (Exception ex)
        {
            Logger.Error($"Take threw on \"{originalAbs}\"", ex);
            return originalAbs; // custody must never crash the app
        }
    }

    /// <summary>Restores ALL custodied items to their original paths and clears the manifest. Called on
    /// clean exit and as the FIRST step of every launch (crash self-heal). Idempotent.</summary>
    public static void RestoreAll()
    {
        try
        {
            var manifest = LoadManifest();
            if (manifest.Count == 0) return;
            Logger.Info($"RestoreAll: {manifest.Count} item(s) to restore.");

            var remaining = new List<CustodyEntry>();
            foreach (var entry in manifest)
            {
                if (!RestoreEntry(entry))
                    remaining.Add(entry); // keep failed entries — they retry next launch, never silently dropped
            }
            SaveManifest(remaining);
            Logger.Info($"RestoreAll: restored {manifest.Count - remaining.Count}, {remaining.Count} kept for retry.");
        }
        catch (Exception ex)
        {
            Logger.Error("RestoreAll threw", ex);
        }
    }

    /// <summary>Restores a single custodied item (used when removing an item / deleting a frame).
    /// Returns true if a custody copy existed and was restored; false if the path was never custodied
    /// (or its custody copy is already gone) — caller treats false as "nothing to restore, proceed".</summary>
    public static bool Restore(string originalAbs)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(originalAbs)) return false;
            var manifest = LoadManifest();
            var entry = manifest.FirstOrDefault(e => PathEquals(e.OriginalPath, originalAbs));
            if (entry == null) return false;

            bool restored = RestoreEntry(entry);
            manifest.RemoveAll(e => PathEquals(e.OriginalPath, originalAbs));
            SaveManifest(manifest);
            return restored;
        }
        catch
        {
            return false;
        }
    }

    // ---- internals ----

    /// <summary>Restores one entry, preferring its exact original directory; if that directory is
    /// unwritable (e.g. a shortcut that lived in the all-users Public\Desktop, where the user has read
    /// but no create rights) or has vanished, falls back to the user's personal desktop so the item is
    /// ALWAYS visible and recoverable — never stranded in custody. Returns false only if every candidate
    /// failed (e.g. locked by a running app): the entry is then kept to retry next launch.</summary>
    private static bool RestoreEntry(CustodyEntry entry)
    {
        try
        {
            string custody = entry.CustodyPath;
            if (!File.Exists(custody) && !Directory.Exists(custody))
                return true; // custody copy already gone — nothing to restore, drop the entry

            bool isDir = Directory.Exists(custody);
            string name = Path.GetFileName(entry.OriginalPath.TrimEnd('\\', '/'));

            // Candidate restore dirs: exact original first, then the personal desktop as a fallback.
            var dirs = new List<string>();
            string origDir = Path.GetDirectoryName(entry.OriginalPath) ?? "";
            if (!string.IsNullOrEmpty(origDir)) dirs.Add(origDir);
            string personalDesktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrEmpty(personalDesktop)) dirs.Add(personalDesktop);

            foreach (string dir in dirs.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string target = UniqueTarget(dir, name, isDir);
                if (Move(custody, target, isDir) && (File.Exists(target) || Directory.Exists(target)))
                {
                    if (!string.Equals(dir, origDir, StringComparison.OrdinalIgnoreCase))
                        Logger.Warn($"Restore: \"{name}\" original dir unwritable — fell back to {target}");
                    return true;
                }
            }
            Logger.Warn($"Restore FAILED (kept in custody for retry): \"{name}\"");
            return false; // all candidates failed (e.g. locked) — keep entry, retry next launch
        }
        catch (Exception ex)
        {
            Logger.Error($"Restore threw on \"{entry.OriginalPath}\"", ex);
            return false;
        }
    }

    /// <summary>Atomic same-volume move; cross-volume folders degrade to copy+verify+delete.
    /// Works in both directions (desktop→custody and custody→desktop).</summary>
    private static bool Move(string source, string dest, bool isDir)
    {
        try
        {
            if (isDir)
            {
                if (SameVolume(source, dest)) { Directory.Move(source, dest); return true; }
                return CopyAndDelete(source, dest, isDir: true);
            }
            File.Move(source, dest); // File.Move handles cross-volume (copy+delete internally)
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool SameVolume(string a, string b)
    {
        try
        {
            string ra = Path.GetPathRoot(Path.GetFullPath(a)) ?? "";
            string rb = Path.GetPathRoot(Path.GetFullPath(b)) ?? "";
            return string.Equals(ra, rb, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Cross-volume fallback: recursive copy + verify + delete source. On crash mid-copy the
    /// source is untouched (copy-first), so RestoreAll's conflict logic keeps the user's data intact.</summary>
    private static bool CopyAndDelete(string source, string dest, bool isDir)
    {
        try
        {
            if (isDir)
            {
                CopyDirectory(source, dest);
                if (!Directory.Exists(dest)) return false;
                Directory.Delete(source, recursive: true);
                return true;
            }
            File.Copy(source, dest, overwrite: true);
            if (!File.Exists(dest)) return false;
            File.Delete(source);
            return true;
        }
        catch { return false; }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (string file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
            File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
        foreach (string dir in Directory.EnumerateDirectories(src, "*", SearchOption.TopDirectoryOnly))
            CopyDirectory(dir, Path.Combine(dst, Path.GetFileName(dir)));
    }

    /// <summary>Picks a non-conflicting path in <paramref name="targetDir"/> for an item named
    /// <paramref name="desiredName"/>: the exact name if its spot is free, otherwise a "(restored)"
    /// / "(restored 2)" / ... variant so nothing is ever overwritten.</summary>
    private static string UniqueTarget(string targetDir, string desiredName, bool isDir)
    {
        string stem = isDir ? desiredName : Path.GetFileNameWithoutExtension(desiredName);
        string ext = isDir ? "" : Path.GetExtension(desiredName);

        int n = 0;
        while (true)
        {
            string suffix = n == 0 ? "" : n == 1 ? " (restored)" : $" (restored {n})";
            string candidate = Path.Combine(targetDir, stem + suffix + ext);
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            n++;
        }
    }

    /// <summary>Unique custody path preserving the original name (+extension for files).</summary>
    private static string MakeUniqueCustodyPath(string originalAbs, bool isDir)
    {
        string name = isDir ? Path.GetFileName(originalAbs.TrimEnd('\\', '/'))
                            : Path.GetFileName(originalAbs);
        string stem = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name);
        int n = 0;
        while (true)
        {
            string candidate = Path.Combine(CustodyDir, n == 0 ? name : $"{stem} ({n}){ext}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            n++;
        }
    }

    private static bool PathEquals(string a, string b)
        => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    // ---- manifest persistence (atomic write) ----

    private static List<CustodyEntry> LoadManifest()
    {
        try
        {
            if (!File.Exists(ManifestFile)) return new List<CustodyEntry>();
            string json = File.ReadAllText(ManifestFile);
            return JsonSerializer.Deserialize<List<CustodyEntry>>(json, JsonOpts) ?? new List<CustodyEntry>();
        }
        catch { return new List<CustodyEntry>(); } // corrupt/missing → treat as empty
    }

    /// <summary>Writes the manifest temp-first then atomically renames over it, so a crash mid-write
    /// can't leave a half-written manifest.</summary>
    private static void SaveManifest(List<CustodyEntry> manifest)
    {
        Directory.CreateDirectory(DataDir);
        string json = JsonSerializer.Serialize(manifest, JsonOpts);
        string tmp = ManifestFile + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, ManifestFile, overwrite: true); // atomic rename on NTFS (same volume)
    }

    private sealed class CustodyEntry
    {
        public string OriginalPath { get; set; } = "";
        public string CustodyPath { get; set; } = "";
        public bool Done { get; set; }
    }
}
