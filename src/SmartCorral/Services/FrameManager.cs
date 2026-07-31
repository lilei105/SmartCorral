using System.Windows;
using System.Linq;
using SmartCorral.Services.Platform;
using SmartCorral.Models;
using SmartCorral.Services.Com;
using SmartCorral.Views;

namespace SmartCorral.Services;

/// <summary>
/// Coordinates the live frame windows and their persisted models. Phase 1a: load -> create windows,
/// add dropped files as shortcut items, persist (bounds + items) to data/frames.json.
/// </summary>
public class FrameManager
{
    public AppData Data { get; private set; } = new();

    /// <summary>Icons per frame row (from settings); frames size to this.</summary>
    public int IconsPerRow { get; set; } = 3;

    /// <summary>Render folders on a separate row within each frame.</summary>
    public bool SeparateFolders { get; set; } = true;

    /// <summary>Keep frames always-on-top (true) or obey normal z-order so other windows can cover them (false).</summary>
    public bool ForceTopmost { get; set; } = true;

    /// <summary>UI zoom for frame contents; on set, syncs into FrameSizer.Scale (drives icon/label/title/sizing).</summary>
    public double UIScale
    {
        get => _uiScale;
        set { _uiScale = value; FrameSizer.Scale = Math.Clamp(value, 0.8, 1.3); }
    }
    private double _uiScale = 1.0;

    private readonly System.Collections.Generic.Dictionary<System.Guid, FrameWindow> _windows = new();

    public void Initialize()
    {
        Data = PersistenceService.Load();

        if (Data.Frames.Count == 0)
        {
            Data.Frames.Add(new DataFrame
            {
                Title = "New Frame",
                X = 160, Y = 160, Width = 380, Height = 240
            });
            PersistenceService.Save(Data);
        }

        foreach (var frame in Data.Frames)
        {
            if (frame is DataFrame df) Open(df);
        }
    }

    private void Open(DataFrame frame)
    {
        var win = new FrameWindow(frame, this);
        win.Left = frame.X;
        win.Top = frame.Y;
        win.Width = frame.Width;
        win.Height = frame.Height;
        _windows[frame.Id] = win;
        win.Topmost = ForceTopmost;
        win.Show();
    }

    /// <summary>Drops real files (from the shell) into a frame.</summary>
    public void AddDroppedFiles(DataFrame frame, string[] files)
    {
        // Skip files already filed (by SourcePath) so a manual drag can't create a duplicate.
        var existing = new HashSet<string>(AllItemSourcePaths(), System.StringComparer.OrdinalIgnoreCase);
        foreach (string file in files)
        {
            if (existing.Contains(file))
            {
                Logger.Info($"AddDroppedFiles: skip already-filed '{System.IO.Path.GetFileName(file)}'.");
                continue;
            }
            AddDesktopFile(frame, file, System.IO.Path.GetFileNameWithoutExtension(file));
        }
        SizeFramesToContent();
        Persist();
    }

    /// <summary>Imports one file as a shortcut item into a frame (shared by manual drop + AI). The
    /// original desktop item is moved into custody so its icon leaves the desktop; the frame's shortcut
    /// points at the custody copy (raw files) or a faithful verbatim copy (.lnk/.url originals).
    /// Returns false (file left on the desktop, NOT filed) if it can't be moved into custody — e.g. a
    /// running executable, or a folder containing one (locked).</summary>
    public bool AddDesktopFile(DataFrame frame, string fullPath, string displayName)
    {
        bool isFolder = System.IO.Directory.Exists(fullPath);
        bool isLink = fullPath.EndsWith(".url", System.StringComparison.OrdinalIgnoreCase);
        bool isShortcutOriginal = fullPath.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase) || isLink;

        // 1. Import FIRST, while the original is still on the desktop: copy .lnk/.url verbatim, or create
        //    a wrapper .lnk for raw files/folders pointing at the desktop path (re-pointed to custody in
        //    step 3). Doing this before Take means an Import failure leaves the file safely on the desktop.
        string relative = ShortcutService.Import(fullPath, displayName);
        string target = ShortcutService.ResolveTarget(relative);
        if (string.IsNullOrEmpty(target)) target = fullPath;

        // 2. Move the real desktop item into custody. If it can't be moved (locked by a running app),
        //    DON'T file a half-item that would show the file in both the frame AND the desktop — undo the
        //    import and leave it on the desktop for the user to close + re-file later.
        string custody = CustodyService.Take(fullPath);
        if (string.Equals(custody, fullPath, System.StringComparison.OrdinalIgnoreCase))
        {
            Logger.Warn($"AddDesktopFile: '{displayName}' couldn't be moved into custody (locked/running?) — left on desktop, not filed.");
            TryDeleteShortcut(relative);
            return false;
        }

        // 3. Raw files/folders: the wrapper .lnk still points at the now-empty desktop path — re-point it
        //    at the custody copy. .lnk/.url originals were copied verbatim and keep their own target.
        if (!isShortcutOriginal)
        {
            ShortcutService.Retarget(relative, custody);
            target = custody;
        }

        frame.Items.Add(new FrameItem
        {
            Filename = relative,
            DisplayName = displayName,
            IsFolder = isFolder,
            IsLink = isLink,
            Target = target,
            SourcePath = fullPath,
            LivePath = custody, // on-disk location of the user's real item right now
            DisplayOrder = frame.Items.Count
        });
        return true;
    }

    /// <summary>Removes a single item from a frame and restores its original desktop file from custody
    /// (non-destructive: the file goes back to the desktop, unlike permanent delete).</summary>
    public void RemoveItem(DataFrame frame, FrameItem item)
    {
        if (!frame.Items.Remove(item)) return;
        CustodyService.Restore(item.SourcePath ?? "");   // back to the desktop (no-op if never custodied)
        TryDeleteShortcut(item.Filename);
        if (_windows.TryGetValue(frame.Id, out var win)) win.RenderItems();
        Persist();
    }

    /// <summary>Re-categorize by drag: moves a FrameItem from whichever frame currently owns it into
    /// <paramref name="target"/>. The custody copy is UNTOUCHED — only the item's frame membership
    /// changes. Returns false (no-op) if the item isn't filed anywhere or already lives in target.</summary>
    public bool MoveItem(DataFrame target, FrameItem item)
    {
        var source = Data.Frames.OfType<DataFrame>().FirstOrDefault(f => f.Items.Contains(item));
        if (source == null || source == target) return false;

        source.Items.Remove(item);
        item.DisplayOrder = target.Items.Count;
        target.Items.Add(item);

        if (_windows.TryGetValue(source.Id, out var sw)) sw.RenderItems();
        if (_windows.TryGetValue(target.Id, out var tw)) tw.RenderItems();
        SizeFramesToContent();
        Persist();
        Logger.Info($"Re-categorized '{item.DisplayName}': 「{source.Title}」 -> 「{target.Title}」");
        return true;
    }

    /// <summary>Re-custodies every already-filed item at launch: RestoreAll put them back on the desktop,
    /// so move each one into a fresh custody path for this session and re-point raw-file wrapper .lnks.
    /// Call after Initialize() and before the windows' icons are relied upon (then RefreshAll).</summary>
    public void RetakeAllIntoCustody()
    {
        foreach (var it in AllItems())
        {
            string src = it.SourcePath ?? "";
            if (System.IO.File.Exists(src) || System.IO.Directory.Exists(src))
            {
                string custody = CustodyService.Take(src);
                it.LivePath = custody;
                bool isShortcutOriginal = src.EndsWith(".lnk", System.StringComparison.OrdinalIgnoreCase)
                                       || src.EndsWith(".url", System.StringComparison.OrdinalIgnoreCase);
                if (!isShortcutOriginal)
                {
                    // The wrapper .lnk from last session still points at the old (now empty) custody path.
                    ShortcutService.Retarget(it.Filename, custody);
                    it.Target = custody;
                }
            }
            else
            {
                // Original no longer on the desktop (user deleted it externally) — degrade to last target.
                it.LivePath = it.Target ?? src;
            }
        }

        // Re-pointing the wrapper .lnk targets invalidated any icons cached against them (the startup
        // render happened while they still pointed at the now-empty previous custody path, so they fell
        // back to the arrowed .lnk icon). Clear so the next render re-extracts from the fresh target.
        IconService.ClearCache();
    }

    /// <summary>Startup data self-heal: drop dead-link items, dedup exact-SourcePath duplicates, delete
    /// orphaned wrapper .lnk, and restore orphaned custody files to the desktop. Run after RetakeAll
    /// and BEFORE the desktop watcher starts, so restored files aren't re-captured by the watcher.</summary>
    public void SweepDataHealth()
    {
        string custodyRoot = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "SmartCorral", "custody");
        string shortcutsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "shortcuts");
        var frames = Data.Frames.OfType<DataFrame>().ToList();
        bool any = false;

        // 1. Dead links: Target points into custody but the file is gone (never matches a .lnk-original
        //    whose Target is the real exe — that's not under custodyRoot).
        foreach (var f in frames)
        {
            var dead = f.Items.Where(it =>
                !string.IsNullOrEmpty(it.Target) &&
                it.Target.StartsWith(custodyRoot, System.StringComparison.OrdinalIgnoreCase) &&
                !System.IO.File.Exists(it.Target) && !System.IO.Directory.Exists(it.Target)).ToList();
            foreach (var d in dead)
            {
                Logger.Info($"Sweep: dead link '{d.DisplayName}' (custody gone) — removing.");
                f.Items.Remove(d); TryDeleteShortcut(d.Filename); any = true;
            }
        }

        // 2. Exact-SourcePath duplicates: keep the first occurrence, drop the rest.
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var f in frames)
        {
            var dups = f.Items.Where(it =>
            {
                if (string.IsNullOrEmpty(it.SourcePath)) return false;
                return !seen.Add(it.SourcePath);
            }).ToList();
            foreach (var d in dups)
            {
                Logger.Info($"Sweep: duplicate SourcePath '{d.SourcePath}' — removing extra '{d.DisplayName}'.");
                f.Items.Remove(d); TryDeleteShortcut(d.Filename); any = true;
            }
        }

        // 2b. Basename duplicates: e.g. a file originally on Public\Desktop was restored to the personal
        //     desktop (write fallback) → its SourcePath differs but the filename is the same → the AI
        //     re-filed it → two items with the same basename. Keep the first, drop the rest.
        var seenNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        foreach (var f in frames)
        {
            var nameDups = f.Items.Where(it =>
            {
                if (string.IsNullOrEmpty(it.SourcePath)) return false;
                return !seenNames.Add(System.IO.Path.GetFileName(it.SourcePath));
            }).ToList();
            foreach (var d in nameDups)
            {
                Logger.Info($"Sweep: duplicate basename '{System.IO.Path.GetFileName(d.SourcePath)}' — removing extra '{d.DisplayName}'.");
                f.Items.Remove(d); TryDeleteShortcut(d.Filename); any = true;
            }
        }

        // 3. Orphaned wrapper .lnk (in shortcuts/ but referenced by no item).
        var usedWrappers = new HashSet<string>(
            frames.SelectMany(f => f.Items).Select(it => it.Filename ?? ""),
            System.StringComparer.OrdinalIgnoreCase);
        if (System.IO.Directory.Exists(shortcutsDir))
        {
            foreach (string file in System.IO.Directory.EnumerateFiles(shortcutsDir))
            {
                string rel = System.IO.Path.Combine("shortcuts", System.IO.Path.GetFileName(file));
                if (!usedWrappers.Contains(rel))
                {
                    Logger.Info($"Sweep: orphaned wrapper '{System.IO.Path.GetFileName(file)}' — deleting.");
                    try { System.IO.File.Delete(file); } catch { }
                    any = true;
                }
            }
        }

        if (any) Persist();

        // 4. Orphaned custody files (no live item references them) → restore to desktop. Run last so
        //    duplicates removed above count as unreferenced. Safe from the watcher (runs at startup
        //    before the watcher starts; restored files predate it, and FSW only fires on later changes).
        //    A custody copy is referenced via Target (raw files) OR LivePath (.lnk originals, whose
        //    Target is the resolved exe, not the custody path) — must check BOTH.
        var referencedCustody = frames.SelectMany(f => f.Items)
            .SelectMany(it => new[] { it.Target ?? "", it.LivePath ?? "" })
            .Where(t => !string.IsNullOrEmpty(t));
        // Filed basenames: orphans matching these are duplicates (from basename dedup) — DON'T restore
        // them to the desktop (would show the file in BOTH a frame AND on the desktop).
        var filedBasenames = frames.SelectMany(f => f.Items)
            .Select(it => it.SourcePath ?? "")
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => System.IO.Path.GetFileName(p));
        CustodyService.RestoreUnreferenced(referencedCustody, filedBasenames);
    }

    private static void TryDeleteShortcut(string relativeFilename)
    {
        try
        {
            string abs = System.IO.Path.Combine(AppContext.BaseDirectory, "data", relativeFilename);
            if (!string.IsNullOrEmpty(relativeFilename) && System.IO.File.Exists(abs))
                System.IO.File.Delete(abs);
        }
        catch { }
    }

    private System.Collections.Generic.IEnumerable<FrameItem> AllItems()
    {
        foreach (var f in Data.Frames.OfType<DataFrame>())
            foreach (var it in f.Items) yield return it;
    }

    public void AddFrame()
    {
        int cols = System.Math.Clamp(IconsPerRow, 2, 8);
        var f = new DataFrame { Title = "New Frame", X = 220, Y = 220, Width = FrameSizer.WidthForColumns(cols), Height = FrameSizer.HeightFor(0, 0, cols) };
        Data.Frames.Add(f);
        Open(f);
        Persist();
    }

    public void DeleteFrame(System.Guid id)
    {
        var f = Data.Frames.FirstOrDefault(x => x.Id == id);
        if (f == null) return;

        // Restore this frame's items from custody back to the desktop before dropping them.
        if (f is DataFrame df)
            foreach (var it in df.Items)
                CustodyService.Restore(it.SourcePath ?? "");

        if (_windows.TryGetValue(id, out var win))
        {
            _windows.Remove(id);
            win.Close();
        }
        Data.Frames.Remove(f);
        Persist();
        if (Data.Frames.Count == 0) AddFrame(); // always keep at least one
    }

    public void RenameFrame(System.Guid id, string name)
    {
        var f = Data.Frames.FirstOrDefault(x => x.Id == id);
        if (f == null) return;
        f.Title = name;
        PersistenceService.Save(Data);
    }

    /// <summary>All source paths already imported across frames (to skip already-filed desktop files).</summary>
    public System.Collections.Generic.IEnumerable<string> AllItemSourcePaths()
    {
        foreach (var f in Data.Frames.OfType<DataFrame>())
            foreach (var it in f.Items)
                if (!string.IsNullOrEmpty(it.SourcePath)) yield return it.SourcePath;
    }

    /// <summary>True if any frame currently holds items (i.e. there's something organized).</summary>
    public bool HasAnyItems() => Data.Frames.OfType<DataFrame>().Any(f => f.Items.Count > 0);

    /// <summary>Create-or-find a DataFrame whose title equals this category.</summary>
    public DataFrame EnsureCategoryFrame(string category)
    {
        var existing = Data.Frames.OfType<DataFrame>().FirstOrDefault(x => x.Title == category);
        if (existing != null) return existing;

        var f = new DataFrame
        {
            Title = category,
            X = 120,
            Y = 120,
            Width = FrameSizer.WidthForColumns(System.Math.Clamp(IconsPerRow, 2, 8)),
            Height = 200
        };
        Data.Frames.Add(f);
        Open(f);
        return f;
    }

    /// <summary>Resizes every frame's height to fit its items (no scroll). Call after items change.</summary>
    public void SizeFramesToContent()
    {
        int cols = System.Math.Clamp(IconsPerRow, 2, 8);
        foreach (var f in Data.Frames.OfType<DataFrame>().ToList())
        {
            f.Width = FrameSizer.WidthForColumns(cols);
            int files, folders;
            if (SeparateFolders)
            {
                files = f.Items.Count(i => !i.IsFolder);
                folders = f.Items.Count(i => i.IsFolder);
            }
            else
            {
                files = f.Items.Count; // all items share one panel when mixed
                folders = 0;
            }
            f.Height = FrameSizer.HeightFor(files, folders, cols);
            if (_windows.TryGetValue(f.Id, out var win)) { win.Width = f.Width; win.Height = f.Height; }
        }
    }

    public void RefreshAll()
    {
        foreach (var win in _windows.Values) win.RenderItems();
    }

    /// <summary>Applies the current ForceTopmost setting to every open frame window.</summary>
    public void ApplyTopmost()
    {
        foreach (var win in _windows.Values) win.Topmost = ForceTopmost;
    }

    /// <summary>Sets Topmost on every open frame window to <paramref name="top"/>. Used by the
    /// "show on Win+D" behavior: frames go topmost when the desktop is the foreground window (so they
    /// survive "Show desktop" without being always-on-top and blocking the user's work).</summary>
    public void SetAllTopmost(bool top)
    {
        foreach (var win in _windows.Values) win.Topmost = top;
        Logger.Info($"SetAllTopmost({top}): {_windows.Count} frame(s).");
        // When un-topmosting: push frames BELOW the foreground window. Without this, frames dropping
        // from topmost land on top of the just-activated app → the app "can't come back to the front."
        if (!top)
            foreach (var win in _windows.Values) win.LowerBelowForeground();
    }

    /// <summary>Right-aligned grid auto-arrange of all frames; moves windows + persists.</summary>
    public void ArrangeAll()
    {
        FrameArranger.Arrange(Data.Frames, MonitorService.WorkAreaForMouse());
        foreach (var (id, win) in _windows)
        {
            var f = Data.Frames.FirstOrDefault(x => x.Id == id);
            if (f != null) { win.Left = f.X; win.Top = f.Y; }
        }
        Persist();
    }

    /// <summary>Screen-space bounds of other open frames (for magnetic snap while dragging).</summary>
    public System.Collections.Generic.IEnumerable<Rect> GetOpenFrameBounds(System.Guid except)
    {
        foreach (var (id, win) in _windows)
            if (id != except) yield return new Rect(win.Left, win.Top, win.ActualWidth > 0 ? win.ActualWidth : win.Width, win.ActualHeight > 0 ? win.ActualHeight : win.Height);
    }

    /// <summary>Re-renders a frame's window after its items changed.</summary>
    public void Refresh(Frame frame)
    {
        if (_windows.TryGetValue(frame.Id, out var win)) win.RenderItems();
    }

    /// <summary>Removes any empty default "New Frame" windows once real frames exist.</summary>
    public void RemoveEmptyDefaultFrames()
    {
        var toRemove = Data.Frames.OfType<DataFrame>()
            .Where(f => f.Title == "New Frame" && f.Items.Count == 0 && Data.Frames.Count > 1)
            .ToList();
        foreach (var f in toRemove)
        {
            if (_windows.TryGetValue(f.Id, out var win)) { _windows.Remove(f.Id); win.Close(); }
            Data.Frames.Remove(f);
        }
    }

    /// <summary>Closes all frames and wipes their items + shortcuts (used by 'Re-organize all'). Restores
    /// every custodied item to the desktop first, so the desktop is repopulated before the AI re-scan.</summary>
    public void ClearAll()
    {
        CustodyService.RestoreAll(); // manifest-tracked items → desktop, manifest cleared
        // Also restore any orphaned custody files (e.g. basename-dedup leftovers whose manifest entry
        // was removed but the file remains). With no filed items, nothing is skipped → all go back.
        CustodyService.RestoreUnreferenced(System.Array.Empty<string>(), System.Array.Empty<string>());

        foreach (var (id, win) in _windows.ToList()) win.Close();
        _windows.Clear();
        Data.Frames.Clear();

        try
        {
            string sd = System.IO.Path.Combine(AppContext.BaseDirectory, "data", "shortcuts");
            if (System.IO.Directory.Exists(sd)) System.IO.Directory.Delete(sd, true);
        }
        catch { }

        Persist();
    }

    /// <summary>Syncs live window bounds back into the models and writes data/frames.json.</summary>
    public void Persist()
    {
        foreach (var (id, win) in _windows)
        {
            var f = Data.Frames.FirstOrDefault(x => x.Id == id);
            if (f == null) continue;
            f.X = win.Left;
            f.Y = win.Top;
            f.Width = win.Width;
            f.Height = win.Height;
        }
        PersistenceService.Save(Data);
    }
}
