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

    /// <summary>Drops real files (from the shell) into a frame: writes .lnk shortcuts, adds items.</summary>
    public void AddDroppedFiles(DataFrame frame, string[] files)
    {
        foreach (string file in files)
        {
            bool isFolder = System.IO.Directory.Exists(file);
            string display = System.IO.Path.GetFileNameWithoutExtension(file);

            string relative = ShortcutService.CreateShortcut(file, display);
            string target = ShortcutService.Resolve(relative);
            if (string.IsNullOrEmpty(target)) target = file;

            frame.Items.Add(new FrameItem
            {
                Filename = relative,
                DisplayName = display,
                IsFolder = isFolder,
                Target = target,
                DisplayOrder = frame.Items.Count
            });
        }

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
