using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace SmartCorral.Services;

/// <summary>
/// Watches the user's Desktop and, after it's been quiet for a few seconds, hands the set of
/// NEWLY-arrived file/folder paths to a callback (which feeds AiOrganizeService.CategorizePathsAsync).
///
/// Only genuinely-new items are reported:
///   • Created/Renamed add a path to the pending set.
///   • Changed only RETIMES the debounce (it never adds a path) — so editing an existing desktop file
///     (a "leftover") does NOT cause it to be categorized. Only an explicit new file does.
///
/// The debounce is "desktop quiet for N seconds": Created/Changed/Renamed each (re)arm a one-shot
/// timer, so a download's repeated writes keep pushing the trigger out until writing stops — we never
/// hand off a half-written file. <see cref="DesktopScanner.IsLikelyTempName"/> filters download temp
/// files (so a lingering .crdownload doesn't fire).
///
/// <see cref="Pause"/>/<see cref="Resume"/> bracket any bulk desktop move (RestoreAll/ClearAll): while
/// paused, events are ignored and the pending set is cleared, so e.g. RestoreAll putting files back on
/// the desktop can't trigger spurious auto-filing.
/// </summary>
public sealed class DesktopWatcher : IDisposable
{
    private const double QuietSeconds = 4.0;

    private readonly FileSystemWatcher _fw;
    private readonly Timer _debounce;
    private readonly Action<IReadOnlyList<string>> _onReady; // invoked on the UI thread
    private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();
    private bool _paused;

    public DesktopWatcher(string desktopPath, Action<IReadOnlyList<string>> onReady)
    {
        _onReady = onReady;
        _fw = new FileSystemWatcher(desktopPath)
        {
            IncludeSubdirectories = false,
            // LastWrite+Size so ongoing writes to a just-created file keep retriggering the debounce.
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        _fw.Created += (_, e) => OnNew(e.FullPath);
        _fw.Renamed += (_, e) => OnNew(e.FullPath);    // a rename's NEW name is a new item
        _fw.Changed += (_, e) => OnWrite(e.FullPath);  // extends the wait only; never adds a path
        // Deleted/Error: ignored.

        _debounce = new Timer(_ => Fire(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() { _fw.EnableRaisingEvents = true; }

    /// <summary>Ignore all events and drop the pending set (use around bulk desktop moves).</summary>
    public void Pause()
    {
        lock (_gate)
        {
            _paused = true;
            _pending.Clear();
            _debounce.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>Resume after <see cref="Pause"/>. Does not auto-arm — the next real event arms the debounce.</summary>
    public void Resume()
    {
        lock (_gate) { _paused = false; }
    }

    // A brand-new item appeared → remember it and (re)arm the debounce.
    private void OnNew(string fullPath)
    {
        if (DesktopScanner.IsLikelyTempName(fullPath)) return; // skip .crdownload / ~$ etc.
        lock (_gate)
        {
            if (_paused) return;
            _pending.Add(fullPath);
            Arm();
        }
    }

    // An existing file was written → only push the trigger out (wait for writes to settle). Crucially,
    // do NOT add the path: editing a leftover must not auto-categorize it.
    private void OnWrite(string fullPath)
    {
        lock (_gate)
        {
            if (_paused || _pending.Count == 0) return; // nothing pending → nothing to wait out
            Arm();
        }
    }

    private void Arm() => _debounce.Change(TimeSpan.FromSeconds(QuietSeconds), Timeout.InfiniteTimeSpan);

    // Timer callback (thread-pool): hand the quiet-Desktop batch to the UI thread.
    private void Fire()
    {
        List<string> batch;
        lock (_gate)
        {
            if (_paused || _pending.Count == 0) return;
            batch = _pending.ToList();
            _pending.Clear();
        }

        // Re-check each path at fire time (a new file may have been deleted, or its temp renamed away).
        var valid = batch
            .Where(p => File.Exists(p) || Directory.Exists(p))
            .Where(p => !DesktopScanner.IsLikelyTempName(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (valid.Count == 0) return;

        Logger.Info($"DesktopWatcher: firing batch of {valid.Count} new item(s).");
        Application.Current?.Dispatcher?.InvokeAsync(() => _onReady(valid));
    }

    public void Dispose()
    {
        _fw.EnableRaisingEvents = false;
        _debounce.Dispose();
        _fw.Dispose();
    }
}
