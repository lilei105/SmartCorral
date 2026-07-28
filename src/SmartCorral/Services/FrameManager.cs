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
        foreach (string file in files)
            AddDesktopFile(frame, file, System.IO.Path.GetFileNameWithoutExtension(file));
        SizeFramesToContent();
        Persist();
    }

    /// <summary>Imports one file as a shortcut item into a frame (shared by manual drop + AI).</summary>
    public void AddDesktopFile(DataFrame frame, string fullPath, string displayName)
    {
        bool isFolder = System.IO.Directory.Exists(fullPath);
        bool isLink = fullPath.EndsWith(".url", System.StringComparison.OrdinalIgnoreCase);

        string relative = ShortcutService.Import(fullPath, displayName);
        string target = ShortcutService.ResolveTarget(relative);
        if (string.IsNullOrEmpty(target)) target = fullPath;

        frame.Items.Add(new FrameItem
        {
            Filename = relative,
            DisplayName = displayName,
            IsFolder = isFolder,
            IsLink = isLink,
            Target = target,
            SourcePath = fullPath,
            DisplayOrder = frame.Items.Count
        });
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

    /// <summary>Closes all frames and wipes their items + shortcuts (used by 'Re-organize all').</summary>
    public void ClearAll()
    {
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
