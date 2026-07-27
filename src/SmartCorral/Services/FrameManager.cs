using System.Linq;
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
        win.Show();
    }

    /// <summary>Drops real files (from the shell) into a frame.</summary>
    public void AddDroppedFiles(DataFrame frame, string[] files)
    {
        foreach (string file in files)
            AddDesktopFile(frame, file, System.IO.Path.GetFileNameWithoutExtension(file));
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
        var f = new DataFrame { Title = "New Frame", X = 220, Y = 220, Width = 360, Height = 240 };
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
            X = 120 + (Data.Frames.Count * 40),
            Y = 120 + (Data.Frames.Count * 30),
            Width = 360,
            Height = 240
        };
        Data.Frames.Add(f);
        Open(f);
        return f;
    }

    /// <summary>Re-renders a frame's window after its items changed.</summary>
    public void Refresh(Frame frame)
    {
        if (_windows.TryGetValue(frame.Id, out var win)) win.RenderItems();
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
